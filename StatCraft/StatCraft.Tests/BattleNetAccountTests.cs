using StatCraft.Models;

namespace StatCraft.Tests;

public class BattleNetAccountTests
{
    [Fact]
    public void DisplayName_ReturnsBattleTag()
    {
        BattleNetAccount account = new BattleNetAccount { BattleTag = "Maru#1234" };
        Assert.Equal("Maru#1234", account.DisplayName);
    }
}
