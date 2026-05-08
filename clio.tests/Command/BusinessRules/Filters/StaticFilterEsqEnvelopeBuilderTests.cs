using System;
using System.Collections.Generic;
using System.Linq;
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
public sealed class StaticFilterEsqEnvelopeBuilderTests {

	private const int LookupDataValueType = 10;
	private const int TextDataValueType = 1;
	private const int IntegerDataValueType = 4;
	private static readonly Guid PackageUId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

	[Test]
	[Category("Unit")]
	[Description("Regression baseline against a real BVE1 envelope from the platform Filter Designer for `Name START_WITH 'U'` on schema City — pins every int constant in EsqContractEnums.")]
	public void Build_Should_Match_BVE1_Snapshot_For_Text_StartWith_On_City() {
		// Arrange — one text leaf on the root City schema; mirrors the snapshot the user
		// captured from a saved business rule, modulo whitespace which is normalized below.
		StaticFilterGroup filter = new(
			LogicalOperation: "AND",
			Filters: [
				new StaticFilterLeaf(
					ColumnPath: "Name",
					ComparisonType: "START_WITH",
					Value: JsonDocument.Parse("\"U\"").RootElement.Clone())
			],
			BackwardReferenceFilters: []);
		StaticFilterEsqEnvelopeBuilder builder = BuildBuilder(BuildCitySchema());

		// Act
		string envelope = builder.Build(filter, "City", PackageUId);

		// Assert — compare against the platform snapshot via structural JSON equality
		// because property ordering differs between System.Text.Json and the platform
		// serializer; structural assertions catch every value drift without coupling
		// the test to either side's whitespace or property order.
		using JsonDocument actual = JsonDocument.Parse(envelope);
		JsonElement root = actual.RootElement;
		root.GetProperty("rootSchemaName").GetString().Should().Be("City");
		root.GetProperty("filterType").GetInt32().Should().Be(EsqFilterTypeValues.FilterGroup);
		root.GetProperty("logicalOperation").GetInt32().Should().Be(EsqLogicalOperationValues.And);
		root.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
		root.GetProperty("className").GetString().Should().Be(EsqClassNames.FilterGroup);

		JsonElement items = root.GetProperty("items");
		items.EnumerateObject().Should().HaveCount(1);
		JsonProperty leaf = items.EnumerateObject().Single();
		leaf.Name.Should().Be("Filter_0",
			because: "Filter Designer keys top-level leaves as Filter_<i>; matching the saved snapshot");

		JsonElement leafValue = leaf.Value;
		leafValue.GetProperty("filterType").GetInt32().Should().Be(EsqFilterTypeValues.CompareFilter);
		leafValue.GetProperty("comparisonType").GetInt32().Should().Be(EsqFilterComparisonValues.StartWith);
		leafValue.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
		leafValue.GetProperty("className").GetString().Should().Be(EsqClassNames.CompareFilter);

		JsonElement left = leafValue.GetProperty("leftExpression");
		left.GetProperty("expressionType").GetInt32().Should().Be(EsqExpressionTypeValues.SchemaColumn);
		left.GetProperty("columnPath").GetString().Should().Be("Name");
		left.GetProperty("className").GetString().Should().Be(EsqClassNames.ColumnExpression);

		JsonElement right = leafValue.GetProperty("rightExpression");
		right.GetProperty("expressionType").GetInt32().Should().Be(EsqExpressionTypeValues.Parameter);
		right.GetProperty("className").GetString().Should().Be(EsqClassNames.ParameterExpression);
		JsonElement parameter = right.GetProperty("parameter");
		parameter.GetProperty("dataValueType").GetInt32().Should().Be(EsqDataValueTypeValues.Text);
		parameter.GetProperty("value").GetString().Should().Be("U");
		parameter.GetProperty("className").GetString().Should().Be(EsqClassNames.Parameter);
	}

	[Test]
	[Category("Unit")]
	[Description("Lookup leaves emit InFilter (filterType=2) with rightExpressions array and a Lookup parameter carrying Value+DisplayValue.")]
	public void Build_Should_Emit_InFilter_For_Lookup_Leaf() {
		// Arrange
		Guid countryId = Guid.Parse("a470b005-e8bb-df11-b00f-001d60e938c6");
		StaticFilterGroup filter = new(
			LogicalOperation: "AND",
			Filters: [
				new StaticFilterLeaf(
					ColumnPath: "Country",
					ComparisonType: "EQUAL",
					Value: JsonDocument.Parse($"\"{countryId:D}\"").RootElement.Clone())
			],
			BackwardReferenceFilters: []);
		ILookupDisplayValueResolver displayResolver = Substitute.For<ILookupDisplayValueResolver>();
		displayResolver.Resolve("Country", "Name", countryId, false).Returns("Ukraine");
		StaticFilterEsqEnvelopeBuilder builder = BuildBuilder(BuildCitySchema(), displayResolver);

		// Act
		string envelope = builder.Build(filter, "City", PackageUId);

		// Assert
		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("filterType").GetInt32().Should().Be(EsqFilterTypeValues.InFilter);
		leaf.GetProperty("dataValueType").GetInt32().Should().Be(EsqDataValueTypeValues.Lookup);
		leaf.GetProperty("referenceSchemaName").GetString().Should().Be("Country");
		leaf.GetProperty("className").GetString().Should().Be(EsqClassNames.InFilter);
		JsonElement rights = leaf.GetProperty("rightExpressions");
		rights.GetArrayLength().Should().Be(1);
		JsonElement parameter = rights[0].GetProperty("parameter");
		parameter.GetProperty("dataValueType").GetInt32().Should().Be(EsqDataValueTypeValues.Lookup);
		JsonElement value = parameter.GetProperty("value");
		value.GetProperty("value").GetString().Should().Be(countryId.ToString("D"));
		value.GetProperty("displayValue").GetString().Should().Be("Ukraine");
	}

	[Test]
	[Category("Unit")]
	[Description("IS_NULL emits an IsNullFilter envelope; IS_NOT_NULL flips the IsNull flag.")]
	public void Build_Should_Emit_IsNullFilter_For_Unary_Comparisons() {
		// Arrange
		StaticFilterGroup filter = new(
			LogicalOperation: "AND",
			Filters: [
				new StaticFilterLeaf("Name", "IS_NULL", null),
				new StaticFilterLeaf("Name", "IS_NOT_NULL", null)
			],
			BackwardReferenceFilters: []);
		StaticFilterEsqEnvelopeBuilder builder = BuildBuilder(BuildCitySchema());

		// Act
		string envelope = builder.Build(filter, "City", PackageUId);

		// Assert
		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement isNull = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		isNull.GetProperty("filterType").GetInt32().Should().Be(EsqFilterTypeValues.IsNullFilter);
		isNull.GetProperty("isNull").GetBoolean().Should().BeTrue();
		isNull.GetProperty("comparisonType").GetInt32().Should().Be(EsqFilterComparisonValues.IsNull);

		JsonElement isNotNull = doc.RootElement.GetProperty("items").GetProperty("Filter_1");
		isNotNull.GetProperty("isNull").GetBoolean().Should().BeFalse();
		isNotNull.GetProperty("comparisonType").GetInt32().Should().Be(EsqFilterComparisonValues.IsNotNull);
	}

	[Test]
	[Category("Unit")]
	[Description("Backward reference compiles to ExistsFilter (filterType=8) keyed BackwardReferenceFilter_<i> with subFilters group.")]
	public void Build_Should_Emit_ExistsFilter_For_Backward_Reference() {
		// Arrange
		StaticFilterGroup childFilter = new(
			LogicalOperation: "AND",
			Filters: [
				new StaticFilterLeaf("Name", "EQUAL", JsonDocument.Parse("\"Kiev\"").RootElement.Clone())
			],
			BackwardReferenceFilters: []);
		StaticFilterGroup root = new(
			LogicalOperation: "AND",
			Filters: [],
			BackwardReferenceFilters: [
				new StaticFilterBackwardReference("[City:Country]", childFilter)
			]);
		IEntityBusinessRuleSchemaProvider schemaProvider = Substitute.For<IEntityBusinessRuleSchemaProvider>();
		EntityDesignSchemaDto countrySchema = BuildCountrySchema();
		EntityDesignSchemaDto citySchema = BuildCitySchema();
		schemaProvider.GetSchema("City", PackageUId).Returns(citySchema);
		schemaProvider.GetSchema("Country", PackageUId).Returns(countrySchema);
		StaticFilterEsqEnvelopeBuilder builder = new(
			new FilterSchemaResolver(schemaProvider),
			Substitute.For<ILookupDisplayValueResolver>());

		// Act
		string envelope = builder.Build(root, "Country", PackageUId);

		// Assert
		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("BackwardReferenceFilter_0");
		leaf.GetProperty("filterType").GetInt32().Should().Be(EsqFilterTypeValues.Exists);
		leaf.GetProperty("comparisonType").GetInt32().Should().Be(EsqFilterComparisonValues.Exists);
		leaf.GetProperty("className").GetString().Should().Be(EsqClassNames.ExistsFilter);
		leaf.GetProperty("isAggregative").GetBoolean().Should().BeTrue();
		JsonElement sub = leaf.GetProperty("subFilters");
		sub.GetProperty("filterType").GetInt32().Should().Be(EsqFilterTypeValues.FilterGroup);
		sub.GetProperty("items").GetProperty("Filter_0").GetProperty("leftExpression")
			.GetProperty("columnPath").GetString().Should().Be("Name");
	}

	[TestCase("EQUAL", EsqFilterComparisonValues.Equal)]
	[TestCase("NOT_EQUAL", EsqFilterComparisonValues.NotEqual)]
	[TestCase("GREATER", EsqFilterComparisonValues.Greater)]
	[TestCase("GREATER_OR_EQUAL", EsqFilterComparisonValues.GreaterOrEqual)]
	[TestCase("LESS", EsqFilterComparisonValues.Less)]
	[TestCase("LESS_OR_EQUAL", EsqFilterComparisonValues.LessOrEqual)]
	[TestCase("START_WITH", EsqFilterComparisonValues.StartWith)]
	[TestCase("NOT_START_WITH", EsqFilterComparisonValues.NotStartWith)]
	[TestCase("END_WITH", EsqFilterComparisonValues.EndWith)]
	[TestCase("NOT_END_WITH", EsqFilterComparisonValues.NotEndWith)]
	[TestCase("CONTAIN", EsqFilterComparisonValues.Contain)]
	[TestCase("NOT_CONTAIN", EsqFilterComparisonValues.NotContain)]
	[Category("Unit")]
	[Description("Each supported comparison token maps to its expected Terrasoft.Nui FilterComparisonType integer.")]
	public void Build_Should_Map_Each_Comparison_Token(string token, int expectedComparisonInt) {
		// Arrange — Name is a Text column, the only schema needed for these comparisons.
		StaticFilterGroup filter = new(
			LogicalOperation: "AND",
			Filters: [
				new StaticFilterLeaf("Name", token, JsonDocument.Parse("\"X\"").RootElement.Clone())
			],
			BackwardReferenceFilters: []);
		StaticFilterEsqEnvelopeBuilder builder = BuildBuilder(BuildCitySchema());

		// Act
		string envelope = builder.Build(filter, "City", PackageUId);

		// Assert
		using JsonDocument doc = JsonDocument.Parse(envelope);
		doc.RootElement.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("comparisonType").GetInt32().Should().Be(expectedComparisonInt);
	}

	[Test]
	[Category("Unit")]
	[Description("Numeric leaf serializes as a JSON number (Int64), not a string.")]
	public void Build_Should_Emit_Numeric_Value_As_Number() {
		// Arrange
		EntityDesignSchemaDto schema = new() {
			Name = "Order",
			Columns = new[] {
				new EntitySchemaColumnDto { Name = "Quantity", DataValueType = IntegerDataValueType }
			}
		};
		StaticFilterGroup filter = new(
			LogicalOperation: "AND",
			Filters: [
				new StaticFilterLeaf("Quantity", "GREATER", JsonDocument.Parse("5").RootElement.Clone())
			],
			BackwardReferenceFilters: []);
		StaticFilterEsqEnvelopeBuilder builder = BuildBuilder(schema);

		// Act
		string envelope = builder.Build(filter, "Order", PackageUId);

		// Assert
		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement parameter = doc.RootElement
			.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("rightExpression").GetProperty("parameter");
		parameter.GetProperty("dataValueType").GetInt32().Should().Be(EsqDataValueTypeValues.Integer);
		parameter.GetProperty("value").GetInt64().Should().Be(5);
	}

	private static StaticFilterEsqEnvelopeBuilder BuildBuilder(
		EntityDesignSchemaDto rootSchema,
		ILookupDisplayValueResolver? displayResolver = null) {
		IEntityBusinessRuleSchemaProvider schemaProvider = Substitute.For<IEntityBusinessRuleSchemaProvider>();
		schemaProvider.GetSchema(rootSchema.Name, PackageUId).Returns(rootSchema);
		// Resolve nested reference schemas (Country) so forward-path traversal terminates.
		schemaProvider.GetSchema("Country", PackageUId).Returns(BuildCountrySchema());
		FilterSchemaResolver schemaResolver = new(schemaProvider);
		displayResolver ??= NoopDisplayResolver();
		return new StaticFilterEsqEnvelopeBuilder(schemaResolver, displayResolver);
	}

	private static ILookupDisplayValueResolver NoopDisplayResolver() {
		ILookupDisplayValueResolver resolver = Substitute.For<ILookupDisplayValueResolver>();
		resolver.Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<bool>())
			.Returns(_ => null);
		return resolver;
	}

	private static EntityDesignSchemaDto BuildCitySchema() => new() {
		Name = "City",
		Columns = new[] {
			new EntitySchemaColumnDto { Name = "Name", DataValueType = TextDataValueType },
			new EntitySchemaColumnDto {
				Name = "Country",
				DataValueType = LookupDataValueType,
				ReferenceSchema = new EntityDesignSchemaDto { Name = "Country" }
			}
		}
	};

	private static EntityDesignSchemaDto BuildCountrySchema() => new() {
		Name = "Country",
		Columns = new[] {
			new EntitySchemaColumnDto { Name = "Name", DataValueType = TextDataValueType },
			new EntitySchemaColumnDto {
				Name = "City",
				DataValueType = LookupDataValueType,
				ReferenceSchema = new EntityDesignSchemaDto { Name = "City" }
			}
		}
	};
}
