namespace FourRVivi.Core.Tools;

public sealed record Snippet(string Category, string Title, string Code);

/// <summary>Ready-to-paste rAthena script snippets, searchable in the UI.</summary>
public static class NpcSnippets
{
    public static IReadOnlyList<Snippet> All { get; } = new[]
    {
        new Snippet("NPC", "Basic dialog NPC",
@"prontera,150,150,4	script	Greeter	4_M_01,{
	mes ""[Greeter]"";
	mes ""Hello, adventurer!"";
	close;
}"),
        new Snippet("NPC", "Warp portal",
@"prontera,155,180,0	warp	toField	2,2,prt_fild08,170,375"),
        new Snippet("NPC", "Simple shop",
@"prontera,160,150,4	shop	MyShop	4_M_01,501:-1,502:-1,503:-1"),
        new Snippet("Reward", "Item exchange (give A, get B)",
@"prontera,162,150,4	script	Exchanger	4_M_01,{
	if (countitem(7227) < 10) { mes ""Need 10 TCG Cards.""; close; }
	delitem 7227,10; getitem 617,1;   // Old Purple Box
	mes ""Done!""; close;
}"),
        new Snippet("Event", "World boss spawn on a clock",
@"-	script	BossClock	-1,{
OnClock2000:
	killmonster ""prontera"",""All"";
	monster ""prontera"",150,150,""Boss Poring"",1002,1,""BossClock::OnDead"";
	announce ""A boss appeared in Prontera!"",bc_all;
	end;
OnDead:
	announce ""The boss was slain!"",bc_all;
	end;
}"),
        new Snippet("GM", "@reward custom command (script-bound)",
@"-	script	atcmd_reward	-1,{
OnInit:
	bindatcmd ""reward"",strnpcinfo(3)+""::OnReward"",99,99;
	end;
OnReward:
	getitem 501,1; dispbottom ""You got a reward!"";
	end;
}"),
        new Snippet("Healer", "Full heal NPC",
@"prontera,158,180,4	script	Healer	4_F_01,{
	percentheal 100,100;
	sc_end SC_ALL;
	mes ""You are fully healed.""; close;
}"),
        new Snippet("Enchant", "Refine-gated reward",
@"prontera,164,150,4	script	RefineReward	4_M_01,{
	if (getrefine() >= 10 && getequipid(EQI_HAND_R) > 0) { getitem 617,1; mes ""Nice +10!""; }
	else mes ""Equip a +10 weapon first."";
	close;
}"),
    };
}
