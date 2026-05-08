using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Schema-aware validator that runs after <see cref="StaticFilterStructuralValidator"/>.
/// Traverses every leaf and backward reference in a <see cref="StaticFilterGroup"/> and,
/// using an <see cref="IFilterSchemaResolver"/> to inspect column metadata, surfaces
/// errors that previously bubbled up through <c>filter.server-rejected</c> when the
/// CrtCopilot LlmEsqConverterService rejected the request:
/// <list type="bullet">
///   <item><description><see cref="BusinessRuleFilterErrorCodes.PathUnknown"/> — column does not exist on the resolved schema</description></item>
///   <item><description><see cref="BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype"/> — comparison token incompatible with the column's data value type</description></item>
///   <item><description><see cref="BusinessRuleFilterErrorCodes.LookupValueNotGuid"/> — lookup leaf value is not parseable as a Guid</description></item>
///   <item><description><see cref="BusinessRuleFilterErrorCodes.BackwardReferenceNot1N"/> — backward-reference path does not target a Lookup column with the expected shape</description></item>
/// </list>
/// </summary>
internal sealed class StaticFilterSchemaAwareValidator(
	IFilterSchemaResolver schemaResolver) {

	public void Validate(StaticFilterGroup filter, string rootSchemaName, Guid packageUId) {
		ArgumentNullException.ThrowIfNull(filter);
		ArgumentException.ThrowIfNullOrWhiteSpace(rootSchemaName);
		ValidateGroup(filter, rootSchemaName, packageUId, StaticFilterStructuralValidator.DefaultFieldPathPrefix);
	}

	private void ValidateGroup(StaticFilterGroup group, string schemaName, Guid packageUId, string fieldPath) {
		if (group.Filters is not null) {
			for (int i = 0; i < group.Filters.Count; i++) {
				ValidateLeaf(group.Filters[i], schemaName, packageUId, $"{fieldPath}.filters[{i}]");
			}
		}
		if (group.BackwardReferenceFilters is not null) {
			for (int i = 0; i < group.BackwardReferenceFilters.Count; i++) {
				ValidateBackwardReference(
					group.BackwardReferenceFilters[i],
					packageUId,
					$"{fieldPath}.backwardReferenceFilters[{i}]");
			}
		}
	}

	private void ValidateLeaf(StaticFilterLeaf leaf, string schemaName, Guid packageUId, string fieldPath) {
		FilterColumnResolution resolution = schemaResolver.Resolve(schemaName, leaf.ColumnPath, packageUId);
		ValidateComparisonAgainstDatatype(leaf, resolution, fieldPath);
		if (IsUnaryComparison(leaf.ComparisonType)) {
			return;
		}
		if (string.Equals(resolution.DataValueTypeName, "Lookup", StringComparison.Ordinal)
			|| string.Equals(resolution.DataValueTypeName, "Guid", StringComparison.Ordinal)) {
			ValidateLookupValueIsGuid(leaf, fieldPath);
		}
	}

	private void ValidateBackwardReference(
		StaticFilterBackwardReference brf,
		Guid packageUId,
		string fieldPath) {
		FilterColumnResolution resolution = schemaResolver.Resolve(
			rootSchemaName: ResolveBackwardSchemaContext(brf, fieldPath),
			columnPath: brf.ReferenceColumnPath,
			packageUId: packageUId);
		if (!resolution.IsBackwardReference || string.IsNullOrWhiteSpace(resolution.BackwardChildSchemaName)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.BackwardReferenceNot1N,
				fieldPath + ".referenceColumnPath",
				$"backward reference '{brf.ReferenceColumnPath}' must use the '[ChildSchema:Column]' shape and target a Lookup column.");
		}
		ValidateGroup(
			brf.Filter,
			resolution.BackwardChildSchemaName!,
			packageUId,
			fieldPath + ".filter");
	}

	// Backward references are validated using the child schema as the resolution context;
	// the resolver itself parses [ChildSchema:Column] and verifies the FK back-link exists.
	// We pass any non-empty string here because the resolver immediately routes through the
	// backward-ref path (the schema-name argument is unused for that branch).
	private static string ResolveBackwardSchemaContext(
		StaticFilterBackwardReference brf,
		string fieldPath) {
		if (string.IsNullOrWhiteSpace(brf.ReferenceColumnPath)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.BackwardReferenceNot1N,
				fieldPath + ".referenceColumnPath",
				"backward reference referenceColumnPath is required.");
		}
		return brf.ReferenceColumnPath;
	}

	private static void ValidateComparisonAgainstDatatype(
		StaticFilterLeaf leaf,
		FilterColumnResolution resolution,
		string fieldPath) {
		string comparison = leaf.ComparisonType;
		string typeName = resolution.DataValueTypeName;
		if (IsTextOnlyComparison(comparison) && !IsTextLikeDataValueType(typeName)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype,
				fieldPath + ".comparisonType",
				$"comparisonType '{comparison}' is only supported for text columns. Column '{leaf.ColumnPath}' has data value type '{typeName}'.");
		}
		if (IsRelationalComparison(comparison) && !IsRelationalDataValueType(typeName)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype,
				fieldPath + ".comparisonType",
				$"comparisonType '{comparison}' is only supported for numeric or date/time columns. Column '{leaf.ColumnPath}' has data value type '{typeName}'.");
		}
	}

	private static void ValidateLookupValueIsGuid(StaticFilterLeaf leaf, string fieldPath) {
		if (!leaf.Value.HasValue || leaf.Value.Value.ValueKind != JsonValueKind.String) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupValueNotGuid,
				fieldPath + ".value",
				$"lookup column '{leaf.ColumnPath}' value must be a JSON string containing a Guid.");
		}
		string? raw = leaf.Value.Value.GetString();
		if (!Guid.TryParse(raw, out _)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupValueNotGuid,
				fieldPath + ".value",
				$"lookup column '{leaf.ColumnPath}' value '{raw}' is not a valid Guid.");
		}
	}

	private static bool IsUnaryComparison(string comparisonType) =>
		string.Equals(comparisonType, "IS_NULL", StringComparison.Ordinal)
		|| string.Equals(comparisonType, "IS_NOT_NULL", StringComparison.Ordinal);

	private static bool IsTextOnlyComparison(string comparisonType) =>
		_textOnlyComparisons.Contains(comparisonType);

	private static bool IsRelationalComparison(string comparisonType) =>
		_relationalComparisons.Contains(comparisonType);

	private static bool IsTextLikeDataValueType(string dataValueTypeName) =>
		BusinessRuleHelpers.IsTextDataValueType(dataValueTypeName);

	private static bool IsRelationalDataValueType(string dataValueTypeName) =>
		BusinessRuleHelpers.IsRelationalDataValueType(dataValueTypeName);

	private static readonly HashSet<string> _textOnlyComparisons = new(StringComparer.Ordinal) {
		"START_WITH", "NOT_START_WITH",
		"END_WITH", "NOT_END_WITH",
		"CONTAIN", "NOT_CONTAIN"
	};

	private static readonly HashSet<string> _relationalComparisons = new(StringComparer.Ordinal) {
		"GREATER", "GREATER_OR_EQUAL", "LESS", "LESS_OR_EQUAL"
	};
}
