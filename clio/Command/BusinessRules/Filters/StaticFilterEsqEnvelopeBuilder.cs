using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Builds a Creatio ESQ filter envelope JSON string from a friendly
/// <see cref="StaticFilterGroup"/> in-process. The envelope shape mirrors what the
/// platform's Filter Designer persists inside <c>BusinessRuleValueExpression.value</c>
/// (BVE1) — confirmed against a real saved snapshot, so any future drift in
/// <see cref="EsqContractEnums"/> integers will surface in the regression test that
/// pins this builder against that snapshot. Replaces the HTTP round-trip to the
/// CrtCopilot-shipped <c>LlmEsqConverterService</c> (the runtime requirement that
/// ENG-88588 closes).
/// </summary>
internal sealed class StaticFilterEsqEnvelopeBuilder(
	IFilterSchemaResolver schemaResolver,
	ILookupDisplayValueResolver lookupDisplayValueResolver) {

	private static readonly JsonSerializerOptions EnvelopeJsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public string Build(StaticFilterGroup filter, string rootSchemaName, Guid packageUId) {
		ArgumentNullException.ThrowIfNull(filter);
		ArgumentException.ThrowIfNullOrWhiteSpace(rootSchemaName);
		EnvelopeFilterGroup envelope = BuildGroup(filter, rootSchemaName, packageUId, isRoot: true);
		return JsonSerializer.Serialize(envelope, EnvelopeJsonOptions);
	}

	private EnvelopeFilterGroup BuildGroup(
		StaticFilterGroup group,
		string schemaName,
		Guid packageUId,
		bool isRoot) {
		Dictionary<string, EnvelopeFilter> items = new(StringComparer.Ordinal);
		// The platform Filter Designer keys top-level leaves as Filter_<i>; the LlmEsqFiltersConverter
		// in CrtCopilot used Filter_<parentIndex>_<i>, but we mirror the Designer shape because that
		// is what the stored regression snapshot captures. Backward references use a parallel sequence.
		if (group.Filters is not null) {
			for (int i = 0; i < group.Filters.Count; i++) {
				items[$"Filter_{i}"] = BuildLeaf(group.Filters[i], schemaName, packageUId);
			}
		}
		if (group.BackwardReferenceFilters is not null) {
			for (int i = 0; i < group.BackwardReferenceFilters.Count; i++) {
				items[$"BackwardReferenceFilter_{i}"] = BuildBackwardReference(
					group.BackwardReferenceFilters[i], packageUId);
			}
		}
		EnvelopeFilterGroup envelope = new() {
			FilterType = EsqFilterTypeValues.FilterGroup,
			LogicalOperation = MapLogicalOperation(group.LogicalOperation),
			IsEnabled = true,
			Items = items,
			Key = string.Empty,
			ClassName = EsqClassNames.FilterGroup
		};
		if (isRoot) {
			envelope.RootSchemaName = schemaName;
		}
		return envelope;
	}

	private EnvelopeFilter BuildLeaf(StaticFilterLeaf leaf, string schemaName, Guid packageUId) {
		FilterColumnResolution resolution = schemaResolver.Resolve(schemaName, leaf.ColumnPath, packageUId);
		if (string.Equals(leaf.ComparisonType, "IS_NULL", StringComparison.Ordinal)
			|| string.Equals(leaf.ComparisonType, "IS_NOT_NULL", StringComparison.Ordinal)) {
			return BuildIsNullFilter(leaf, resolution);
		}
		if (string.Equals(resolution.DataValueTypeName, "Lookup", StringComparison.Ordinal)) {
			return BuildInFilter(leaf, resolution, packageUId);
		}
		return BuildCompareFilter(leaf, resolution);
	}

	private static EnvelopeFilter BuildIsNullFilter(StaticFilterLeaf leaf, FilterColumnResolution resolution) {
		bool isNull = string.Equals(leaf.ComparisonType, "IS_NULL", StringComparison.Ordinal);
		return new EnvelopeFilter {
			FilterType = EsqFilterTypeValues.IsNullFilter,
			ComparisonType = isNull ? EsqFilterComparisonValues.IsNull : EsqFilterComparisonValues.IsNotNull,
			IsEnabled = true,
			IsNull = isNull,
			LeftExpression = BuildColumnExpression(resolution.NormalizedColumnPath),
			Key = string.Empty,
			ClassName = EsqClassNames.IsNullFilter
		};
	}

	private static EnvelopeFilter BuildCompareFilter(StaticFilterLeaf leaf, FilterColumnResolution resolution) {
		object? typedValue = ConvertParameterValue(leaf.Value, resolution.DataValueTypeName);
		return new EnvelopeFilter {
			FilterType = EsqFilterTypeValues.CompareFilter,
			ComparisonType = MapComparison(leaf.ComparisonType),
			IsEnabled = true,
			LeftExpression = BuildColumnExpression(resolution.NormalizedColumnPath),
			RightExpression = BuildParameterExpression(resolution.DataValueTypeId, typedValue),
			Key = string.Empty,
			ClassName = EsqClassNames.CompareFilter
		};
	}

	private EnvelopeFilter BuildInFilter(
		StaticFilterLeaf leaf,
		FilterColumnResolution resolution,
		Guid packageUId) {
		Guid recordId = ParseLookupGuid(leaf);
		string? displayValue = ResolveDisplayValue(resolution.ReferenceSchemaName, recordId, packageUId);
		EnvelopeExpression rightExpression = new() {
			ExpressionType = EsqExpressionTypeValues.Parameter,
			ClassName = EsqClassNames.ParameterExpression,
			Parameter = new EnvelopeParameter {
				DataValueType = EsqDataValueTypeValues.Lookup,
				Value = new EnvelopeLookupValue {
					Value = recordId.ToString("D"),
					DisplayValue = displayValue
				},
				ClassName = EsqClassNames.Parameter
			}
		};
		return new EnvelopeFilter {
			FilterType = EsqFilterTypeValues.InFilter,
			ComparisonType = MapComparison(leaf.ComparisonType),
			IsEnabled = true,
			LeftExpression = BuildColumnExpression(resolution.NormalizedColumnPath),
			RightExpressions = [rightExpression],
			IsAggregative = false,
			DataValueType = EsqDataValueTypeValues.Lookup,
			ReferenceSchemaName = resolution.ReferenceSchemaName,
			Key = string.Empty,
			ClassName = EsqClassNames.InFilter
		};
	}

	private EnvelopeFilter BuildBackwardReference(StaticFilterBackwardReference brf, Guid packageUId) {
		FilterColumnResolution resolution = schemaResolver.Resolve(
			rootSchemaName: brf.ReferenceColumnPath,
			columnPath: brf.ReferenceColumnPath,
			packageUId: packageUId);
		if (!resolution.IsBackwardReference || string.IsNullOrWhiteSpace(resolution.BackwardChildSchemaName)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.BackwardReferenceNot1N,
				StaticFilterStructuralValidator.DefaultFieldPathPrefix + ".backwardReferenceFilters[*]",
				$"backward reference '{brf.ReferenceColumnPath}' did not resolve to a 1:N relationship.");
		}
		EnvelopeFilterGroup subFilters = BuildGroup(
			brf.Filter,
			resolution.BackwardChildSchemaName!,
			packageUId,
			isRoot: false);
		return new EnvelopeFilter {
			FilterType = EsqFilterTypeValues.Exists,
			ComparisonType = EsqFilterComparisonValues.Exists,
			IsEnabled = true,
			IsAggregative = true,
			LeftExpression = BuildColumnExpression(resolution.NormalizedColumnPath),
			SubFilters = subFilters,
			Key = string.Empty,
			ClassName = EsqClassNames.ExistsFilter
		};
	}

	private string? ResolveDisplayValue(string? referenceSchemaName, Guid recordId, Guid packageUId) {
		if (string.IsNullOrWhiteSpace(referenceSchemaName)) {
			return null;
		}
		string displayColumnName = ResolveDisplayColumnName(referenceSchemaName, packageUId);
		return lookupDisplayValueResolver.Resolve(referenceSchemaName, displayColumnName, recordId, throwIfMissing: false);
	}

	private string ResolveDisplayColumnName(string referenceSchemaName, Guid packageUId) {
		// Probe the reference schema by resolving its synthetic primary-display path so the schema
		// is fetched exactly once via the cache inside the resolver. Falls back to "Name" — the
		// canonical PrimaryDisplayColumn for ~all out-of-the-box Creatio schemas — when the
		// resolver cannot find an explicit display column on the schema.
		try {
			schemaResolver.Resolve(referenceSchemaName, "Name", packageUId);
			return "Name";
		} catch (BusinessRuleFilterException) {
			return "Id";
		}
	}

	private static EnvelopeExpression BuildColumnExpression(string columnPath) =>
		new() {
			ExpressionType = EsqExpressionTypeValues.SchemaColumn,
			ColumnPath = columnPath,
			ClassName = EsqClassNames.ColumnExpression
		};

	private static EnvelopeExpression BuildParameterExpression(int dataValueTypeId, object? value) =>
		new() {
			ExpressionType = EsqExpressionTypeValues.Parameter,
			ClassName = EsqClassNames.ParameterExpression,
			Parameter = new EnvelopeParameter {
				DataValueType = dataValueTypeId,
				Value = value,
				ClassName = EsqClassNames.Parameter
			}
		};

	private static int MapLogicalOperation(string logicalOperation) =>
		string.Equals(logicalOperation, "OR", StringComparison.OrdinalIgnoreCase)
			? EsqLogicalOperationValues.Or
			: EsqLogicalOperationValues.And;

	private static int MapComparison(string token) =>
		token switch {
			"EQUAL" => EsqFilterComparisonValues.Equal,
			"NOT_EQUAL" => EsqFilterComparisonValues.NotEqual,
			"GREATER" => EsqFilterComparisonValues.Greater,
			"GREATER_OR_EQUAL" => EsqFilterComparisonValues.GreaterOrEqual,
			"LESS" => EsqFilterComparisonValues.Less,
			"LESS_OR_EQUAL" => EsqFilterComparisonValues.LessOrEqual,
			"START_WITH" => EsqFilterComparisonValues.StartWith,
			"NOT_START_WITH" => EsqFilterComparisonValues.NotStartWith,
			"END_WITH" => EsqFilterComparisonValues.EndWith,
			"NOT_END_WITH" => EsqFilterComparisonValues.NotEndWith,
			"CONTAIN" => EsqFilterComparisonValues.Contain,
			"NOT_CONTAIN" => EsqFilterComparisonValues.NotContain,
			"IS_NULL" => EsqFilterComparisonValues.IsNull,
			"IS_NOT_NULL" => EsqFilterComparisonValues.IsNotNull,
			_ => throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonUnknown,
				StaticFilterStructuralValidator.DefaultFieldPathPrefix + ".comparisonType",
				$"comparisonType '{token}' is not supported.")
		};

	private static Guid ParseLookupGuid(StaticFilterLeaf leaf) {
		if (!leaf.Value.HasValue || leaf.Value.Value.ValueKind != JsonValueKind.String) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupValueNotGuid,
				StaticFilterStructuralValidator.DefaultFieldPathPrefix + ".value",
				$"lookup column '{leaf.ColumnPath}' value must be a Guid string.");
		}
		string raw = leaf.Value.Value.GetString() ?? string.Empty;
		if (!Guid.TryParse(raw, out Guid value)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupValueNotGuid,
				StaticFilterStructuralValidator.DefaultFieldPathPrefix + ".value",
				$"lookup column '{leaf.ColumnPath}' value '{raw}' is not a valid Guid.");
		}
		return value;
	}

	private static object? ConvertParameterValue(JsonElement? value, string dataValueTypeName) {
		if (!value.HasValue || value.Value.ValueKind == JsonValueKind.Null) {
			return null;
		}
		JsonElement el = value.Value;
		switch (dataValueTypeName) {
			case "Boolean":
				return el.ValueKind == JsonValueKind.True;
			case "Integer":
				return el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out long longValue)
					? longValue
					: throw NewShape("Integer column expects a JSON integer.");
			case "Float":
			case "Money":
			case "Float0":
			case "Float1":
			case "Float2":
			case "Float3":
			case "Float4":
			case "Float8":
				return el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out decimal decimalValue)
					? decimalValue
					: throw NewShape("Numeric column expects a JSON number.");
			case "Date":
			case "DateTime":
			case "Time":
				return el.ValueKind == JsonValueKind.String
					? el.GetString()
					: throw NewShape($"{dataValueTypeName} column expects an ISO-8601 string value.");
			case "Guid":
				return el.ValueKind == JsonValueKind.String
					&& Guid.TryParse(el.GetString(), out Guid guid)
					? guid.ToString("D")
					: throw NewShape("Guid column expects a Guid string value.");
			default:
				return el.ValueKind switch {
					JsonValueKind.String => el.GetString(),
					JsonValueKind.Number when el.TryGetInt64(out long lv) => lv,
					JsonValueKind.Number when el.TryGetDecimal(out decimal dv) => dv,
					JsonValueKind.True => true,
					JsonValueKind.False => false,
					_ => el.GetRawText()
				};
		}

		static BusinessRuleFilterException NewShape(string message) =>
			new(BusinessRuleFilterErrorCodes.ValueShape,
				StaticFilterStructuralValidator.DefaultFieldPathPrefix + ".value",
				message);
	}

	#region Wire DTOs (envelope shape)

	private sealed class EnvelopeFilterGroup : EnvelopeFilter {
		[JsonPropertyName("rootSchemaName")]
		public string? RootSchemaName { get; set; }
	}

	private class EnvelopeFilter {
		[JsonPropertyName("filterType")]
		public int FilterType { get; set; }

		[JsonPropertyName("comparisonType")]
		public int? ComparisonType { get; set; }

		[JsonPropertyName("logicalOperation")]
		public int? LogicalOperation { get; set; }

		[JsonPropertyName("isEnabled")]
		public bool IsEnabled { get; set; }

		[JsonPropertyName("isNull")]
		public bool? IsNull { get; set; }

		[JsonPropertyName("isAggregative")]
		public bool? IsAggregative { get; set; }

		[JsonPropertyName("dataValueType")]
		public int? DataValueType { get; set; }

		[JsonPropertyName("referenceSchemaName")]
		public string? ReferenceSchemaName { get; set; }

		[JsonPropertyName("leftExpression")]
		public EnvelopeExpression? LeftExpression { get; set; }

		[JsonPropertyName("rightExpression")]
		public EnvelopeExpression? RightExpression { get; set; }

		[JsonPropertyName("rightExpressions")]
		public IReadOnlyList<EnvelopeExpression>? RightExpressions { get; set; }

		[JsonPropertyName("subFilters")]
		public EnvelopeFilterGroup? SubFilters { get; set; }

		[JsonPropertyName("items")]
		public IReadOnlyDictionary<string, EnvelopeFilter>? Items { get; set; }

		[JsonPropertyName("key")]
		public string? Key { get; set; }

		[JsonPropertyName("className")]
		public string? ClassName { get; set; }
	}

	private sealed class EnvelopeExpression {
		[JsonPropertyName("expressionType")]
		public int ExpressionType { get; set; }

		[JsonPropertyName("columnPath")]
		public string? ColumnPath { get; set; }

		[JsonPropertyName("parameter")]
		public EnvelopeParameter? Parameter { get; set; }

		[JsonPropertyName("className")]
		public string? ClassName { get; set; }
	}

	private sealed class EnvelopeParameter {
		[JsonPropertyName("dataValueType")]
		public int DataValueType { get; set; }

		[JsonPropertyName("value")]
		public object? Value { get; set; }

		[JsonPropertyName("className")]
		public string? ClassName { get; set; }
	}

	private sealed class EnvelopeLookupValue {
		[JsonPropertyName("value")]
		public string Value { get; set; } = string.Empty;

		[JsonPropertyName("displayValue")]
		public string? DisplayValue { get; set; }
	}

	#endregion
}
