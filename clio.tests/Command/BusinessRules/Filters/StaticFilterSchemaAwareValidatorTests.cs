using System;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules.Filters;

[TestFixture]
[Property("Module", "Command.BusinessRules.Filters")]
public sealed class StaticFilterSchemaAwareValidatorTests {

	private const int LookupDataValueType = 10;
	private const int TextDataValueType = 1;
	private const int IntegerDataValueType = 4;
	private static readonly Guid PackageUId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

	[Test]
	[Category("Unit")]
	[Description("Valid filter passes schema-aware validation without throwing.")]
	public void Validate_Should_Pass_For_Happy_Path() {
		StaticFilterSchemaAwareValidator validator = BuildValidator();
		StaticFilterGroup filter = new(
			"AND",
			[new StaticFilterLeaf("Name", "START_WITH", JsonDocument.Parse("\"U\"").RootElement.Clone())],
			[]);

		Action act = () => validator.Validate(filter, "City", PackageUId);

		act.Should().NotThrow();
	}

	[Test]
	[Category("Unit")]
	[Description("Unknown column path throws BusinessRuleFilterException with filter.path-unknown.")]
	public void Validate_Should_Reject_Unknown_Column_Path() {
		StaticFilterSchemaAwareValidator validator = BuildValidator();
		StaticFilterGroup filter = new(
			"AND",
			[new StaticFilterLeaf("DoesNotExist", "EQUAL", JsonDocument.Parse("\"x\"").RootElement.Clone())],
			[]);

		Action act = () => validator.Validate(filter, "City", PackageUId);

		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.PathUnknown);
	}

	[Test]
	[Category("Unit")]
	[Description("START_WITH on a non-text column throws filter.comparison-not-supported-for-datatype.")]
	public void Validate_Should_Reject_Text_Comparison_On_Numeric_Column() {
		StaticFilterSchemaAwareValidator validator = BuildValidator();
		StaticFilterGroup filter = new(
			"AND",
			[new StaticFilterLeaf("Quantity", "START_WITH", JsonDocument.Parse("\"5\"").RootElement.Clone())],
			[]);

		Action act = () => validator.Validate(filter, "City", PackageUId);

		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype);
	}

	[Test]
	[Category("Unit")]
	[Description("Lookup leaf with a non-Guid string value throws filter.lookup-value-not-guid.")]
	public void Validate_Should_Reject_Lookup_Value_Not_Guid() {
		StaticFilterSchemaAwareValidator validator = BuildValidator();
		StaticFilterGroup filter = new(
			"AND",
			[new StaticFilterLeaf("Country", "EQUAL", JsonDocument.Parse("\"Ukraine\"").RootElement.Clone())],
			[]);

		Action act = () => validator.Validate(filter, "City", PackageUId);

		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.LookupValueNotGuid);
	}

	[Test]
	[Category("Unit")]
	[Description("Lookup leaf with a valid Guid string value passes lookup validation.")]
	public void Validate_Should_Accept_Lookup_Value_Guid() {
		StaticFilterSchemaAwareValidator validator = BuildValidator();
		StaticFilterGroup filter = new(
			"AND",
			[new StaticFilterLeaf(
				"Country", "EQUAL",
				JsonDocument.Parse("\"a470b005-e8bb-df11-b00f-001d60e938c6\"").RootElement.Clone())],
			[]);

		Action act = () => validator.Validate(filter, "City", PackageUId);

		act.Should().NotThrow();
	}

	[Test]
	[Category("Unit")]
	[Description("Backward reference filters recursively validate their nested filter group against the child schema context.")]
	public void Validate_Should_Recurse_Into_Backward_Reference_SubFilters() {
		StaticFilterSchemaAwareValidator validator = BuildValidator();
		StaticFilterGroup nested = new(
			"AND",
			[new StaticFilterLeaf("Name", "EQUAL", JsonDocument.Parse("\"Kiev\"").RootElement.Clone())],
			[]);
		StaticFilterGroup root = new(
			"AND",
			[],
			[new StaticFilterBackwardReference("[City:Country]", nested)]);

		Action act = () => validator.Validate(root, "Country", PackageUId);

		act.Should().NotThrow();
	}

	private static StaticFilterSchemaAwareValidator BuildValidator() {
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
				new EntitySchemaColumnDto { Name = "Quantity", DataValueType = IntegerDataValueType },
				new EntitySchemaColumnDto {
					Name = "Country",
					DataValueType = LookupDataValueType,
					ReferenceSchema = countrySchema
				}
			}
		};
		provider.GetSchema("City", PackageUId).Returns(citySchema);
		provider.GetSchema("Country", PackageUId).Returns(countrySchema);
		return new StaticFilterSchemaAwareValidator(new FilterSchemaResolver(provider));
	}
}
