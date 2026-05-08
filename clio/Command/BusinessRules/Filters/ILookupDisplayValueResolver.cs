using System;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Resolves the human-readable display value for a lookup record by its primary key.
/// The static-filter envelope embeds <c>{ Value: <guid>, DisplayValue: "&lt;name&gt;" }</c>
/// inside lookup parameters — the runtime UI reads <c>DisplayValue</c> back when
/// rendering applied filters, so omitting it produces a working but unlabelled chip.
/// Implementations should cache resolved display values inside the lifetime of a single
/// envelope build so repeated lookups on the same (schema, id) avoid round-tripping.
/// </summary>
internal interface ILookupDisplayValueResolver {

	/// <summary>
	/// Returns the display value for <paramref name="recordId"/> on
	/// <paramref name="referenceSchemaName"/> using <paramref name="displayColumnName"/>
	/// as the column to project (caller resolves it from the reference schema's
	/// <c>PrimaryDisplayColumn</c>). Returns <c>null</c> when the record was not
	/// found and <paramref name="throwIfMissing"/> is <c>false</c>; otherwise throws
	/// <see cref="BusinessRuleFilterException"/> with
	/// <see cref="BusinessRuleFilterErrorCodes.LookupRecordNotFound"/>.
	/// </summary>
	string? Resolve(
		string referenceSchemaName,
		string displayColumnName,
		Guid recordId,
		bool throwIfMissing = true);
}
