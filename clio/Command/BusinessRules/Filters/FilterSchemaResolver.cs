using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Default <see cref="IFilterSchemaResolver"/> backed by the existing
/// <see cref="IEntityBusinessRuleSchemaProvider"/>. Each call to <see cref="Resolve"/>
/// allocates a fresh per-call schema cache so a filter group with several leaves on
/// the same root or reference schema fetches each schema at most once. Trailing-Id
/// suffixes (<c>AccountId</c> → <c>Account</c>) are stripped to mirror the platform
/// runtime, which exposes lookup columns under their bare name.
/// </summary>
internal sealed class FilterSchemaResolver(
	IEntityBusinessRuleSchemaProvider schemaProvider)
	: IFilterSchemaResolver {

	public FilterColumnResolution Resolve(string rootSchemaName, string columnPath, Guid packageUId) {
		ArgumentException.ThrowIfNullOrWhiteSpace(rootSchemaName);
		ArgumentException.ThrowIfNullOrWhiteSpace(columnPath);

		Dictionary<string, EntityDesignSchemaDto> schemaCache =
			new(StringComparer.OrdinalIgnoreCase);

		string trimmedPath = columnPath.Trim();
		if (TryParseBackwardReference(trimmedPath, out string? childSchemaName, out string? childColumnName)) {
			ValidateBackwardReference(childSchemaName!, childColumnName!, packageUId, schemaCache, columnPath);
			return new FilterColumnResolution(
				DataValueTypeName: "Lookup",
				DataValueTypeId: EsqDataValueTypeValues.Lookup,
				ReferenceSchemaName: null,
				NormalizedColumnPath: trimmedPath,
				IsBackwardReference: true,
				BackwardChildSchemaName: childSchemaName,
				BackwardChildColumnName: childColumnName);
		}

		string[] segments = trimmedPath.Split(
			'.',
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (segments.Length == 0) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.PathUnknown,
				StaticFilterStructuralValidator.DefaultFieldPathPrefix,
				$"columnPath '{columnPath}' is empty.");
		}

		string currentSchemaName = rootSchemaName;
		string normalizedAccumulator = string.Empty;
		EntitySchemaColumnDto? currentColumn = null;

		for (int i = 0; i < segments.Length; i++) {
			IReadOnlyDictionary<string, EntitySchemaColumnDto> columns =
				GetColumns(currentSchemaName, packageUId, schemaCache);
			string requestedSegment = segments[i];
			string normalizedSegment = NormalizeColumnSegment(requestedSegment);
			if (!TryFindColumn(columns, normalizedSegment, out EntitySchemaColumnDto? column)
				&& !TryFindColumn(columns, requestedSegment, out column)) {
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.PathUnknown,
					StaticFilterStructuralValidator.DefaultFieldPathPrefix,
					$"columnPath segment '{requestedSegment}' was not found on schema '{currentSchemaName}'.");
			}
			currentColumn = column!;
			normalizedAccumulator = normalizedAccumulator.Length == 0
				? column!.Name
				: normalizedAccumulator + "." + column!.Name;

			bool isLastSegment = i == segments.Length - 1;
			if (isLastSegment) {
				continue;
			}

			string? referenceSchemaName = column!.ReferenceSchema?.Name;
			if (string.IsNullOrWhiteSpace(referenceSchemaName)) {
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.PathUnknown,
					StaticFilterStructuralValidator.DefaultFieldPathPrefix,
					$"columnPath segment '{column.Name}' on schema '{currentSchemaName}' is not a Lookup; the path cannot continue past it.");
			}
			currentSchemaName = referenceSchemaName!;
		}

		string dataValueTypeName = BusinessRuleHelpers.MapDataValueTypeName(currentColumn!.DataValueType);
		return new FilterColumnResolution(
			DataValueTypeName: dataValueTypeName,
			DataValueTypeId: currentColumn.DataValueType ?? throw new InvalidOperationException(
				$"Resolved column '{currentColumn.Name}' has no dataValueType."),
			ReferenceSchemaName: currentColumn.ReferenceSchema?.Name,
			NormalizedColumnPath: normalizedAccumulator,
			IsBackwardReference: false,
			BackwardChildSchemaName: null,
			BackwardChildColumnName: null);
	}

	// `[ChildSchema:ColumnOnChild]` is the canonical backward-reference shape produced by
	// the static filter contract; trailing `Id` suffixes are normalized off the column
	// name so the wire path matches the runtime FK property.
	private static bool TryParseBackwardReference(
		string columnPath,
		out string? childSchemaName,
		out string? childColumnName) {
		childSchemaName = null;
		childColumnName = null;
		if (columnPath.Length < 4 || columnPath[0] != '[' || columnPath[^1] != ']') {
			return false;
		}
		string inner = columnPath[1..^1];
		int separatorIndex = inner.IndexOf(':');
		if (separatorIndex <= 0 || separatorIndex >= inner.Length - 1) {
			return false;
		}
		childSchemaName = inner[..separatorIndex].Trim();
		string rawColumn = inner[(separatorIndex + 1)..].Trim();
		if (string.IsNullOrEmpty(childSchemaName) || string.IsNullOrEmpty(rawColumn)) {
			return false;
		}
		childColumnName = NormalizeColumnSegment(rawColumn);
		return true;
	}

	private void ValidateBackwardReference(
		string childSchemaName,
		string childColumnName,
		Guid packageUId,
		Dictionary<string, EntityDesignSchemaDto> schemaCache,
		string originalColumnPath) {
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columns =
			GetColumns(childSchemaName, packageUId, schemaCache);
		if (!TryFindColumn(columns, childColumnName, out EntitySchemaColumnDto? column)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.BackwardReferenceNot1N,
				StaticFilterStructuralValidator.DefaultFieldPathPrefix,
				$"backward-reference column '{childColumnName}' was not found on schema '{childSchemaName}'.");
		}
		if (string.IsNullOrWhiteSpace(column!.ReferenceSchema?.Name)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.BackwardReferenceNot1N,
				StaticFilterStructuralValidator.DefaultFieldPathPrefix,
				$"backward-reference '{originalColumnPath}' targets non-Lookup column '{childColumnName}' on schema '{childSchemaName}'.");
		}
	}

	private IReadOnlyDictionary<string, EntitySchemaColumnDto> GetColumns(
		string schemaName,
		Guid packageUId,
		Dictionary<string, EntityDesignSchemaDto> schemaCache) {
		if (schemaCache.TryGetValue(schemaName, out EntityDesignSchemaDto? cachedSchema)) {
			return BusinessRuleHelpers.BuildColumnMap(cachedSchema);
		}
		EntityDesignSchemaDto schema = schemaProvider.GetSchema(schemaName, packageUId);
		schemaCache[schemaName] = schema;
		return BusinessRuleHelpers.BuildColumnMap(schema);
	}

	private static bool TryFindColumn(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columns,
		string columnName,
		out EntitySchemaColumnDto? column) {
		if (columns.TryGetValue(columnName, out EntitySchemaColumnDto? value)) {
			column = value;
			return true;
		}
		// EntitySchema column maps are case-sensitive by default; fall back to a tolerant
		// lookup so caller-supplied paths with off-by-case segments still resolve.
		KeyValuePair<string, EntitySchemaColumnDto> match = columns
			.FirstOrDefault(pair => string.Equals(pair.Key, columnName, StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(match.Key)) {
			column = match.Value;
			return true;
		}
		column = null;
		return false;
	}

	private static string NormalizeColumnSegment(string segment) {
		// Mirrors LlmColumnPathHelper.NormalizeColumnPath: strip the FK 'Id' suffix so callers
		// can pass either AccountId or Account and resolve to the same lookup column. The
		// suffix has to be exactly 'Id' on a segment longer than two characters; literally
		// any segment named 'Id' (the schema's primary key) must remain unchanged.
		if (segment.Length > 2 && segment.EndsWith("Id", StringComparison.Ordinal)) {
			return segment[..^2];
		}
		return segment;
	}
}
