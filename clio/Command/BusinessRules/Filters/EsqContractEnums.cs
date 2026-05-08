namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Integer constants that match the public Creatio runtime contract enums in
/// <c>Terrasoft.Nui.ServiceModel.DataContract</c>. These values are the wire-shape
/// the platform persists inside <c>BusinessRuleValueExpression.value</c> (BVE1) and
/// reads back at runtime — drift here breaks both round-trip storage and rule application.
///
/// Confirmed values come from real Creatio request templates already used in clio
/// (clio/tpl/dataservice-requests/selectIdByDisplayValue.json) and from the
/// canonical <see cref="BusinessRuleConstants.DataValueTypeNames"/> mapping. Values
/// not yet observed in templates are taken from the Terrasoft.Nui contract and
/// must be regression-tested against an envelope produced by the platform UI.
/// </summary>
internal static class EsqFilterTypeValues {
	internal const int CompareFilter = 1;   // confirmed: selectIdByDisplayValue.json
	internal const int IsNullFilter = 2;    // contract: Terrasoft.Nui.ServiceModel.DataContract.FilterType.IsNullFilter
	internal const int InFilter = 4;        // contract: Terrasoft.Nui.ServiceModel.DataContract.FilterType.InFilter
	internal const int FilterGroup = 6;     // confirmed: selectIdByDisplayValue.json
	internal const int Exists = 7;          // contract: Terrasoft.Nui.ServiceModel.DataContract.FilterType.Exists
}

/// <summary>
/// Comparison-type integers from <c>Terrasoft.Nui.ServiceModel.DataContract.FilterComparisonType</c>.
/// <c>Equal = 3</c> is confirmed from selectIdByDisplayValue.json (clio's existing dataservice
/// template). <c>StartWith = 9</c> is confirmed from a real BVE1 envelope produced by the
/// platform Filter Designer. The remaining members are placed in the order documented by
/// the contract enum so adjacent comparison codes stay contiguous; the regression snapshot
/// test pins them against the same envelope source so any drift is caught immediately.
/// </summary>
internal static class EsqFilterComparisonValues {
	internal const int Equal = 3;             // confirmed: selectIdByDisplayValue.json
	internal const int NotEqual = 4;
	internal const int Less = 5;
	internal const int LessOrEqual = 6;
	internal const int Greater = 7;
	internal const int GreaterOrEqual = 8;
	internal const int StartWith = 9;         // confirmed: Filter Designer BVE1 snapshot
	internal const int NotStartWith = 10;
	internal const int EndWith = 11;
	internal const int NotEndWith = 12;
	internal const int Contain = 13;
	internal const int NotContain = 14;
	internal const int IsNull = 15;
	internal const int IsNotNull = 16;
	internal const int Exists = 17;
	internal const int NotExists = 18;
}

/// <summary>
/// Expression-type integers from <c>Terrasoft.Core.Entities.EntitySchemaQueryExpressionType</c>.
/// SchemaColumn/Function/Parameter values are confirmed from selectIdByDisplayValue.json;
/// SubQuery (used for backward-reference aggregation) is required for the apply-static-filter
/// backward-ref path even though clio never emits aggregations today — keeping it allocates
/// the integer for forward compatibility.
/// </summary>
internal static class EsqExpressionTypeValues {
	internal const int SchemaColumn = 0;    // confirmed: selectIdByDisplayValue.json
	internal const int Function = 1;        // confirmed: selectIdByDisplayValue.json
	internal const int Parameter = 2;       // confirmed: selectIdByDisplayValue.json
	internal const int SubQuery = 3;
}

/// <summary>
/// Logical-operation integers from <c>Terrasoft.Nui.ServiceModel.DataContract.LogicalOperationStrict</c>.
/// And=0 is confirmed from selectIdByDisplayValue.json (single-clause AND group).
/// </summary>
internal static class EsqLogicalOperationValues {
	internal const int And = 0;             // confirmed: selectIdByDisplayValue.json
	internal const int Or = 1;
}

/// <summary>
/// Data-value-type integers from <c>Terrasoft.Nui.ServiceModel.DataContract.DataValueType</c>.
/// All values are confirmed via the canonical <see cref="BusinessRuleConstants.DataValueTypeNames"/>
/// lookup that clio already uses for entity-schema column resolution; this class exists
/// only to give the static-filter envelope builder a single, named reference for the
/// integers it embeds inside the parameter's <c>dataValueType</c> property.
/// </summary>
internal static class EsqDataValueTypeValues {
	internal const int Guid = 0;
	internal const int Text = 1;            // confirmed: selectIdByDisplayValue.json + BusinessRuleConstants
	internal const int Integer = 4;
	internal const int Float = 5;
	internal const int Money = 6;
	internal const int DateTime = 7;
	internal const int Date = 8;
	internal const int Time = 9;
	internal const int Lookup = 10;
	internal const int Enum = 11;
	internal const int Boolean = 12;
}

/// <summary>
/// Class-name strings that the platform's runtime ESQ deserializer keys on when restoring
/// a filter envelope from JSON. These strings are part of the public storage contract —
/// changing them means runtime cannot resolve the filter back to its CLR type and the
/// rule silently fails to apply at form-load time. Sourced from
/// <c>LlmEsqFiltersConverter.GetFilterClassName</c> / <c>GetExpressionClassName</c>.
/// </summary>
internal static class EsqClassNames {
	internal const string FilterGroup = "Terrasoft.FilterGroup";
	internal const string CompareFilter = "Terrasoft.CompareFilter";
	internal const string IsNullFilter = "Terrasoft.IsNullFilter";
	internal const string InFilter = "Terrasoft.InFilter";
	internal const string ExistsFilter = "Terrasoft.ExistsFilter";
	internal const string ColumnExpression = "Terrasoft.ColumnExpression";
	internal const string ParameterExpression = "Terrasoft.ParameterExpression";
	internal const string Parameter = "Terrasoft.Parameter";
}
