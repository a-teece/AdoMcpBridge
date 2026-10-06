using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WorkItemFieldGuardTests
{
    public static TheoryData<string> LongTextFields() => new(BasicToolGuardrails.LongTextFieldRefNames);

    [Theory]
    [MemberData(nameof(LongTextFields))]
    public void Guard_LongTextField_Throws_WithSlotToolName(string fieldRefName)
    {
        var act = () => WorkItemFieldGuard.ThrowIfForbidden(fieldRefName);

        act.Should().Throw<CallerArgumentException>()
            .Which.Message.Should().Contain(fieldRefName)
            .And.Contain("ado_bridge_create_upload_slot")
            .And.Contain("ado_bridge_write_field_from_slot");
    }

    [Theory]
    [InlineData("system.description")]
    [InlineData("MICROSOFT.VSTS.TCM.REPROSTEPS")]
    [InlineData("  System.Description  ")]
    public void Guard_LongTextField_IsMatchedCaseAndWhitespaceInsensitively(string fieldRefName)
    {
        var act = () => WorkItemFieldGuard.ThrowIfForbidden(fieldRefName);

        act.Should().Throw<CallerArgumentException>()
            .Which.Message.Should().Contain("ado_bridge_write_field_from_slot");
    }

    [Theory]
    [InlineData("System.Parent")]
    [InlineData("system.parent")]
    [InlineData(" System.Parent ")]
    public void Guard_SystemParent_Throws_WithLinkToolName(string fieldRefName)
    {
        var act = () => WorkItemFieldGuard.ThrowIfForbidden(fieldRefName);

        act.Should().Throw<CallerArgumentException>()
            .Which.Message.Should().Contain("System.Parent").And.Contain("ado_bridge_wit_link");
    }

    [Theory]
    [InlineData("System.Title")]
    [InlineData("System.State")]
    [InlineData("System.Tags")]
    [InlineData("System.AssignedTo")]
    [InlineData("Microsoft.VSTS.Common.Priority")]
    public void Guard_TitleStateTags_Pass(string fieldRefName)
    {
        var act = () => WorkItemFieldGuard.ThrowIfForbidden(fieldRefName);

        act.Should().NotThrow();
    }
}
