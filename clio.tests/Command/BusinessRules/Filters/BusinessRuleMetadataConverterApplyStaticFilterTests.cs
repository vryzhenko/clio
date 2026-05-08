using System;
using System.Collections.Generic;
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
public sealed class BusinessRuleMetadataConverterApplyStaticFilterTests {

	private const int LookupDataValueType = 10;
	private const int TextDataValueType = 1;
	private static readonly Guid PackageUId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

	[Test]
	[Category("Unit")]
	[Description("Embeds the in-process envelope as a JSON STRING in BusinessRuleValueExpression.value — matches the platform's compressed BVE1 storage format (escape-once string).")]
	public void ToMetadata_Should_Embed_Envelope_As_Escaped_Json_String() {
		// Arrange
		StaticFilterEsqEnvelopeBuilder builder = BuildEnvelopeBuilder();

		// Act
		BusinessRuleMetadataDto result = BusinessRuleMetadataConverter.ToMetadata(
			BuildColumnMap(),
			BuildRule(),
			builder,
			"UsrContact",
			PackageUId);

		// Assert
		string serialized = JsonSerializer.Serialize(result.Cases[0].Actions[0], BusinessRuleConstants.JsonOptions);
		using JsonDocument doc = JsonDocument.Parse(serialized);
		JsonElement valueExpr = doc.RootElement.GetProperty("value");
		valueExpr.GetProperty("typeName").GetString()
			.Should().Be(BusinessRuleConstants.BusinessRuleValueExpressionTypeName);
		JsonElement valueProp = valueExpr.GetProperty("value");
		valueProp.ValueKind.Should().Be(JsonValueKind.String,
			because: "platform stores BusinessRuleValueExpression.value as an escaped JSON string (BVE1)");
		string envelopeJson = valueProp.GetString()!;
		envelopeJson.Should().Contain("\"rootSchemaName\":\"City\"",
			because: "the action targets a Lookup whose reference schema is City");
		envelopeJson.Should().Contain("\"filterType\":6",
			because: "outer envelope is a FilterGroup");
	}

	[Test]
	[Category("Unit")]
	[Description("Apply-static-filter expression emits only typeName/uId/type/path — no extra empty-string hints.")]
	public void ToMetadata_Should_Emit_Minimal_Expression() {
		// Arrange
		StaticFilterEsqEnvelopeBuilder builder = BuildEnvelopeBuilder();

		// Act
		BusinessRuleMetadataDto result = BusinessRuleMetadataConverter.ToMetadata(
			BuildColumnMap(),
			BuildRule(),
			builder,
			"UsrContact",
			PackageUId);

		// Assert
		string serialized = JsonSerializer.Serialize(result.Cases[0].Actions[0], BusinessRuleConstants.JsonOptions);
		using JsonDocument doc = JsonDocument.Parse(serialized);
		JsonElement expression = doc.RootElement.GetProperty("expression");
		expression.GetProperty("path").GetString().Should().Be("UsrCity");
		expression.GetProperty("type").GetString().Should().Be("AttributeValue");
		expression.TryGetProperty("dataValueTypeName", out _).Should().BeFalse();
		expression.TryGetProperty("referenceSchemaName", out _).Should().BeFalse();
	}

	[Test]
	[Category("Unit")]
	[Description("Throws when the converter is invoked without a StaticFilterEsqEnvelopeBuilder; apply-static-filter requires it.")]
	public void ToMetadata_Should_Throw_When_Builder_Is_Missing() {
		// Act
		Action act = () => BusinessRuleMetadataConverter.ToMetadata(BuildColumnMap(), BuildRule());

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*StaticFilterEsqEnvelopeBuilder*");
	}

	[Test]
	[Category("Unit")]
	[Description("Apply-static-filter rule with no `condition` produces a case with no condition group and a single DataLoaded trigger — matches the platform editor's default for unconditional static filters.")]
	public void ToMetadata_Should_Allow_Apply_Static_Filter_Without_Condition() {
		// Arrange
		StaticFilterEsqEnvelopeBuilder builder = BuildEnvelopeBuilder();
		BusinessRule rule = new() {
			Caption = "Always restrict City",
			Condition = null,
			Actions = [
				new ApplyStaticFilterBusinessRuleAction {
					TargetAttribute = "UsrCity",
					Filter = JsonDocument.Parse(
						"{\"logicalOperation\":\"AND\",\"filters\":[]}").RootElement.Clone()
				}
			]
		};

		// Act
		BusinessRuleMetadataDto result = BusinessRuleMetadataConverter.ToMetadata(
			BuildColumnMap(),
			rule,
			builder,
			"UsrContact",
			PackageUId);

		// Assert
		result.Cases[0].Condition.Should().BeNull(
			because: "no condition was supplied; the case must carry no condition group");
		result.Triggers.Should().HaveCount(1,
			because: "without per-attribute conditions, only the DataLoaded trigger is emitted");
		result.Triggers[0].Type.Should().Be(BusinessRuleConstants.DataLoadedTriggerType);
	}

	[Test]
	[Category("Unit")]
	[Description("Page-level conversion rejects apply-static-filter actions; the action is only valid for entity-level rules.")]
	public void ToPageMetadata_Should_Reject_Apply_Static_Filter() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			BusinessRuleHelpers.BuildAttributeDescriptorMap(BuildColumnMap());

		// Act
		Action act = () => BusinessRuleMetadataConverter.ToPageMetadata(attributeMap, BuildRule());

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("apply-static-filter is not supported in page-level business rules.");
	}

	private static StaticFilterEsqEnvelopeBuilder BuildEnvelopeBuilder() {
		// City schema exposes a Country lookup; filling out the reference graph keeps the
		// resolver from short-circuiting and lets the builder embed a realistic envelope.
		IEntityBusinessRuleSchemaProvider schemaProvider = Substitute.For<IEntityBusinessRuleSchemaProvider>();
		EntityDesignSchemaDto citySchema = new() {
			Name = "City",
			Columns = new[] {
				new EntitySchemaColumnDto {
					Name = "Name",
					DataValueType = TextDataValueType
				},
				new EntitySchemaColumnDto {
					Name = "Country",
					DataValueType = LookupDataValueType,
					ReferenceSchema = new EntityDesignSchemaDto { Name = "Country" }
				}
			}
		};
		EntityDesignSchemaDto countrySchema = new() {
			Name = "Country",
			Columns = new[] {
				new EntitySchemaColumnDto { Name = "Name", DataValueType = TextDataValueType },
				new EntitySchemaColumnDto { Name = "Id", DataValueType = 0 }
			}
		};
		schemaProvider.GetSchema("City", PackageUId).Returns(citySchema);
		schemaProvider.GetSchema("Country", PackageUId).Returns(countrySchema);
		FilterSchemaResolver schemaResolver = new(schemaProvider);
		ILookupDisplayValueResolver displayResolver = Substitute.For<ILookupDisplayValueResolver>();
		displayResolver.Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<bool>())
			.Returns(_ => null);
		return new StaticFilterEsqEnvelopeBuilder(schemaResolver, displayResolver);
	}

	private static BusinessRule BuildRule() {
		ApplyStaticFilterBusinessRuleAction action = new() {
			TargetAttribute = "UsrCity",
			Filter = JsonDocument.Parse(
				"{\"logicalOperation\":\"AND\",\"filters\":[" +
				"{\"columnPath\":\"Country\",\"comparisonType\":\"EQUAL\"," +
				"\"value\":\"a470b005-e8bb-df11-b00f-001d60e938c6\"}]}").RootElement.Clone()
		};
		BusinessRuleCondition condition = new() {
			LeftExpression = new BusinessRuleExpression {
				Type = "AttributeValue",
				Path = "UsrName"
			},
			ComparisonType = "is-filled-in"
		};
		BusinessRuleConditionGroup conditionGroup = new() {
			LogicalOperation = "AND",
			Conditions = [condition]
		};
		return new BusinessRule {
			Caption = "Restrict City to Ukraine",
			Condition = conditionGroup,
			Actions = [action]
		};
	}

	private static IReadOnlyDictionary<string, EntitySchemaColumnDto> BuildColumnMap() {
		Dictionary<string, EntitySchemaColumnDto> map = new(StringComparer.OrdinalIgnoreCase) {
			["UsrCity"] = new EntitySchemaColumnDto {
				Name = "UsrCity",
				DataValueType = LookupDataValueType,
				ReferenceSchema = new EntityDesignSchemaDto { Name = "City" }
			},
			["UsrName"] = new EntitySchemaColumnDto {
				Name = "UsrName",
				DataValueType = TextDataValueType
			}
		};
		return map;
	}
}
