using System;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Resolves a filter <c>columnPath</c> against the entity schema graph and returns
/// metadata needed by <see cref="StaticFilterSchemaAwareValidator"/> and
/// <see cref="StaticFilterEsqEnvelopeBuilder"/> to emit a valid ESQ envelope.
/// Responsible for forward-path traversal (<c>Country.Name</c> → load Country schema → find Name)
/// and for parsing backward-reference shapes (<c>[ChildSchema:ColumnOnChild]</c>).
/// Implementations cache schema lookups inside a single resolver instance so a deep
/// path or a filter group with many leaves on the same schema produces only one
/// EntitySchemaDesigner round-trip.
/// </summary>
internal interface IFilterSchemaResolver {

	/// <summary>
	/// Resolves the column described by <paramref name="columnPath"/> evaluated relative
	/// to <paramref name="rootSchemaName"/>. Throws <see cref="BusinessRuleFilterException"/>
	/// with <see cref="BusinessRuleFilterErrorCodes.PathUnknown"/> when any segment of the
	/// path cannot be resolved on the corresponding schema.
	/// </summary>
	FilterColumnResolution Resolve(string rootSchemaName, string columnPath, Guid packageUId);
}

/// <summary>
/// Result of resolving a <c>columnPath</c> against an entity schema.
/// For forward paths (<c>City.Country.Name</c>) <see cref="DataValueTypeName"/> describes
/// the leaf column and <see cref="ReferenceSchemaName"/> is non-null when the leaf itself
/// is a Lookup. For backward references (<c>[Activity:Account]</c>) <see cref="IsBackwardReference"/>
/// is true and <see cref="BackwardChildSchemaName"/> / <see cref="BackwardChildColumnName"/>
/// expose the parsed fragments; <see cref="DataValueTypeName"/> is "Lookup" because a
/// backward reference is a 1:N relationship by definition.
/// </summary>
internal sealed record FilterColumnResolution(
	string DataValueTypeName,
	int DataValueTypeId,
	string? ReferenceSchemaName,
	string NormalizedColumnPath,
	bool IsBackwardReference,
	string? BackwardChildSchemaName,
	string? BackwardChildColumnName);
