using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Default <see cref="ILookupDisplayValueResolver"/> backed by the platform's
/// <c>DataService</c> SelectQuery endpoint. The query mirrors the shape of
/// <c>clio/tpl/dataservice-requests/selectIdByDisplayValue.json</c> (the existing
/// clio template that round-trips against the same enum constants in
/// <see cref="EsqContractEnums"/>) so the wire format stays consistent across clio.
/// </summary>
internal sealed class LookupDisplayValueResolver(
	IApplicationClient applicationClient)
	: ILookupDisplayValueResolver {

	private const string ServiceName = "DataService";
	private const string ServiceMethod = "SelectQuery";
	private const string FilterFieldPath = "rule.actions[*].filter";

	private static readonly JsonSerializerOptions RequestJsonOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);

	public string? Resolve(
		string referenceSchemaName,
		string displayColumnName,
		Guid recordId,
		bool throwIfMissing = true) {
		ArgumentException.ThrowIfNullOrWhiteSpace(referenceSchemaName);
		ArgumentException.ThrowIfNullOrWhiteSpace(displayColumnName);
		string cacheKey = $"{referenceSchemaName}:{recordId:D}";
		if (_cache.TryGetValue(cacheKey, out string? cached)) {
			return MaybeThrow(cached, referenceSchemaName, recordId, throwIfMissing);
		}
		string requestBody = BuildSelectByIdRequest(referenceSchemaName, displayColumnName, recordId);
		string rawResponse = applicationClient.CallConfigurationService(
			ServiceName, ServiceMethod, requestBody);
		string? displayValue = ParseFirstRowDisplayValue(rawResponse);
		_cache[cacheKey] = displayValue;
		return MaybeThrow(displayValue, referenceSchemaName, recordId, throwIfMissing);
	}

	private static string? MaybeThrow(string? value, string schema, Guid id, bool throwIfMissing) {
		if (value is not null || !throwIfMissing) {
			return value;
		}
		throw new BusinessRuleFilterException(
			BusinessRuleFilterErrorCodes.LookupRecordNotFound,
			FilterFieldPath,
			$"Lookup record with id '{id:D}' was not found on reference schema '{schema}'.");
	}

	private static string BuildSelectByIdRequest(string schemaName, string displayColumnName, Guid recordId) {
		// Wire shape mirrors clio/tpl/dataservice-requests/selectIdByDisplayValue.json:
		// outer FilterGroup with one CompareFilter on Id, columns = { Display, Id }.
		// The caller passes the resolved PrimaryDisplayColumn name from the schema
		// metadata it already loaded for the parent forward-path resolution.
		object request = new {
			rootSchemaName = schemaName,
			filters = new {
				isEnabled = true,
				filterType = EsqFilterTypeValues.FilterGroup,
				logicalOperation = EsqLogicalOperationValues.And,
				items = new Dictionary<string, object> {
					["byId"] = new {
						filterType = EsqFilterTypeValues.CompareFilter,
						comparisonType = EsqFilterComparisonValues.Equal,
						isEnabled = true,
						leftExpression = new {
							expressionType = EsqExpressionTypeValues.SchemaColumn,
							columnPath = "Id"
						},
						rightExpression = new {
							expressionType = EsqExpressionTypeValues.Parameter,
							parameter = new {
								dataValueType = EsqDataValueTypeValues.Guid,
								value = recordId.ToString("D"),
								className = EsqClassNames.Parameter
							},
							className = EsqClassNames.ParameterExpression
						}
					}
				}
			},
			useLocalization = true,
			columns = new {
				items = new Dictionary<string, object> {
					["Display"] = new {
						expression = new {
							expressionType = EsqExpressionTypeValues.SchemaColumn,
							columnPath = displayColumnName
						}
					},
					["Id"] = new {
						expression = new {
							expressionType = EsqExpressionTypeValues.SchemaColumn,
							columnPath = "Id"
						}
					}
				}
			}
		};
		return JsonSerializer.Serialize(request, RequestJsonOptions);
	}

	private static string? ParseFirstRowDisplayValue(string rawResponse) {
		if (string.IsNullOrWhiteSpace(rawResponse)) {
			return null;
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(rawResponse);
			if (!doc.RootElement.TryGetProperty("rows", out JsonElement rows)
				|| rows.ValueKind != JsonValueKind.Array
				|| rows.GetArrayLength() == 0) {
				return null;
			}
			JsonElement firstRow = rows[0];
			if (firstRow.TryGetProperty("Display", out JsonElement display)
				&& display.ValueKind == JsonValueKind.String) {
				return display.GetString();
			}
			return null;
		} catch (JsonException) {
			return null;
		}
	}
}
