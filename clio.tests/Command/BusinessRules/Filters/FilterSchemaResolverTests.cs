using System;
using Clio.Command.BusinessRules;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules.Filters;

[TestFixture]
[Property("Module", "Command.BusinessRules.Filters")]
public sealed class FilterSchemaResolverTests {

	private const int LookupDataValueType = 10;
	private const int TextDataValueType = 1;
	private static readonly Guid PackageUId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

	[Test]
	[Category("Unit")]
	[Description("Single-segment forward path resolves to the named column on the root schema.")]
	public void Resolve_Should_Return_Root_Column_For_Single_Segment_Path() {
		IEntityBusinessRuleSchemaProvider provider = BuildProvider();
		FilterSchemaResolver resolver = new(provider);

		FilterColumnResolution result = resolver.Resolve("City", "Name", PackageUId);

		result.DataValueTypeName.Should().Be("Text");
		result.NormalizedColumnPath.Should().Be("Name");
		result.IsBackwardReference.Should().BeFalse();
		result.ReferenceSchemaName.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("Multi-segment forward path follows the lookup chain into the referenced schema and returns the leaf column metadata.")]
	public void Resolve_Should_Follow_Forward_Reference_Chain() {
		IEntityBusinessRuleSchemaProvider provider = BuildProvider();
		FilterSchemaResolver resolver = new(provider);

		FilterColumnResolution result = resolver.Resolve("City", "Country.Name", PackageUId);

		result.DataValueTypeName.Should().Be("Text",
			because: "Country.Name terminates on the Text column on the Country schema");
		result.NormalizedColumnPath.Should().Be("Country.Name");
	}

	[Test]
	[Category("Unit")]
	[Description("Per-call schema cache: a path that revisits the same schema fetches it exactly once.")]
	public void Resolve_Should_Cache_Schema_Lookups_Per_Call() {
		IEntityBusinessRuleSchemaProvider provider = BuildProvider();
		FilterSchemaResolver resolver = new(provider);

		resolver.Resolve("City", "Country.Name", PackageUId);

		provider.Received(1).GetSchema("City", PackageUId);
		provider.Received(1).GetSchema("Country", PackageUId);
	}

	[Test]
	[Category("Unit")]
	[Description("Backward-reference shape `[ChildSchema:Column]` is parsed into IsBackwardReference + child schema name + child column name.")]
	public void Resolve_Should_Parse_Backward_Reference_Shape() {
		IEntityBusinessRuleSchemaProvider provider = BuildProvider();
		FilterSchemaResolver resolver = new(provider);

		FilterColumnResolution result = resolver.Resolve("ignored", "[City:Country]", PackageUId);

		result.IsBackwardReference.Should().BeTrue();
		result.BackwardChildSchemaName.Should().Be("City");
		result.BackwardChildColumnName.Should().Be("Country");
	}

	[Test]
	[Category("Unit")]
	[Description("Unknown forward-path segment surfaces filter.path-unknown via BusinessRuleFilterException.")]
	public void Resolve_Should_Throw_PathUnknown_For_Missing_Column() {
		IEntityBusinessRuleSchemaProvider provider = BuildProvider();
		FilterSchemaResolver resolver = new(provider);

		Action act = () => resolver.Resolve("City", "Bogus", PackageUId);

		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.PathUnknown);
	}

	[Test]
	[Category("Unit")]
	[Description("Trailing `Id` suffix on a lookup column is normalized so 'CountryId' resolves to the bare 'Country' column.")]
	public void Resolve_Should_Normalize_Trailing_Id_Suffix() {
		IEntityBusinessRuleSchemaProvider provider = BuildProvider();
		FilterSchemaResolver resolver = new(provider);

		FilterColumnResolution result = resolver.Resolve("City", "CountryId", PackageUId);

		result.DataValueTypeName.Should().Be("Lookup");
		result.NormalizedColumnPath.Should().Be("Country");
	}

	private static IEntityBusinessRuleSchemaProvider BuildProvider() {
		IEntityBusinessRuleSchemaProvider provider = Substitute.For<IEntityBusinessRuleSchemaProvider>();
		EntityDesignSchemaDto countrySchema = new() {
			Name = "Country",
			Columns = new[] {
				new EntitySchemaColumnDto { Name = "Name", DataValueType = TextDataValueType }
			}
		};
		EntityDesignSchemaDto citySchema = new() {
			Name = "City",
			Columns = new[] {
				new EntitySchemaColumnDto { Name = "Name", DataValueType = TextDataValueType },
				new EntitySchemaColumnDto {
					Name = "Country",
					DataValueType = LookupDataValueType,
					ReferenceSchema = countrySchema
				}
			}
		};
		provider.GetSchema("City", PackageUId).Returns(citySchema);
		provider.GetSchema("Country", PackageUId).Returns(countrySchema);
		return provider;
	}
}
