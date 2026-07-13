using FourRVivi.Core.Automation;
using FourRVivi.Core.Data;
using FourRVivi.Core.Game;
using FourRVivi.Core.Grf;
using FourRVivi.Core.Tools;
using FourRVivi.Core.Trackers;
using Xunit;

namespace FourRVivi.Core.Tests;

public class StatCalculatorTests
{
    [Fact]
    public void Computes_core_stats()
    {
        var r = StatCalculator.Compute(new CalcInput { BaseLevel = 99, Str = 99, Dex = 50, Luk = 50, WeaponAtk = 100 });
        Assert.True(r["ATK"] > 100);
        Assert.True(r["HIT"] >= 99 + 50);
        Assert.True(r["CRIT"] >= 1);
        Assert.Contains("~Max HP", r.Keys);
    }
}

public class MvpEntryTests
{
    [Fact]
    public void Unkilled_shows_dash() => Assert.Equal("—", new MvpEntry().Status());

    [Fact]
    public void Recent_kill_is_pending()
    {
        var e = new MvpEntry { MinMinutes = 60, MaxMinutes = 70, KilledAt = DateTime.Now };
        var status = e.Status();
        Assert.StartsWith("not yet", status);
        Assert.Contains("in ", status);
    }
}

public class BuffTimerTests
{
    [Fact]
    public void Idle_until_started() => Assert.Equal("idle", new BuffTimer().Display);

    [Fact]
    public void Counts_down_after_start()
    {
        var b = new BuffTimer { DurationSec = 120 };
        b.Start();
        Assert.InRange(b.RemainingSec, 118, 120);
    }
}

public class ChainMacroTests
{
    [Fact]
    public void Holds_steps()
    {
        var m = new ChainMacro { Name = "vend" };
        m.Steps.Add(new ChainStep { Key = "F9" });
        Assert.Single(m.Steps);
        Assert.Equal("F9", m.Steps[0].Key);
    }
}

public class SprWriterTests
{
    [Fact]
    public void Truecolor_writer_round_trips_with_reader()
    {
        var rgba = new byte[]
        {
            255, 0, 0, 255,
            0, 255, 0, 128
        };
        var spr = SprWriter.WriteTrueColor(new[] { new SpriteFrame(2, 1, rgba) });
        var frames = SprReader.Decode(spr);

        Assert.Single(frames);
        Assert.Equal(2, frames[0].Width);
        Assert.Equal(1, frames[0].Height);
        Assert.Equal(rgba, frames[0].Rgba);
    }
}

public class LiveSceneTests
{
    [Fact]
    public void Keeps_recent_monster_when_world_scrolls_and_one_frame_misses()
    {
        var scene = LiveScene.Instance;
        scene.Clear();
        scene.Active = true;

        scene.SetEntities(new[]
        {
            new SceneItem(100, 120, 40, 32, "Poring", 0.91f),
            new SceneItem(300, 130, 42, 34, "Lunatic", 0.90f),
        }, clientCoords: true);
        scene.SetEntities(new[]
        {
            new SceneItem(90, 120, 40, 32, "Poring", 0.92f),
            new SceneItem(290, 130, 42, 34, "Lunatic", 0.91f),
        }, clientCoords: true);

        scene.SetEntities(new[]
        {
            new SceneItem(270, 130, 42, 34, "Lunatic", 0.93f),
        }, clientCoords: true);

        Assert.Contains(scene.Entities, e => e.Label == "Poring" && e.Cx < 120);
        scene.Clear();
    }

    [Fact]
    public void Monitor_mode_entities_keep_track_identity_between_frames()
    {
        var scene = LiveScene.Instance;
        scene.Clear();
        scene.Active = true;

        scene.SetEntities(new[]
        {
            new SceneItem(220, 160, 36, 30, "Familiar", 0.88f),
        }, clientCoords: false);
        var first = scene.Entities.Single();

        scene.SetEntities(new[]
        {
            new SceneItem(224, 162, 36, 30, "Familiar", 0.91f),
        }, clientCoords: false);
        var second = scene.Entities.Single();

        Assert.False(scene.ClientCoords);
        Assert.Equal(first.TrackId, second.TrackId);
        Assert.True(second.Hits >= 2);
        scene.Clear();
    }
}

public class GameDatabaseMapTests
{
    [Theory]
    [InlineData("mob__farmiliar", "Familiar")]
    [InlineData("mob__farmiliar_03", "Familiar")]
    [InlineData("spr_monsters__zombie_18", "Zombie")]
    [InlineData("mob__red_plant", "Red Plant")]
    [InlineData("mob__poporing", "Poporing")]
    public void Training_monster_labels_resolve_to_display_names(string trainedLabel, string expected)
    {
        var db = new GameDatabase();

        Assert.Equal(expected, db.MonsterDisplayNameFromTrainingLabel(trainedLabel));
    }

    [Fact]
    public void Payon_dungeon_focus_contains_common_spawn_mobs()
    {
        var db = new GameDatabase();
        var names = db.MapMonsterSpawns("pay_dun00")
            .Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Skeleton", names);
        Assert.Contains("Zombie", names);
        Assert.Contains("Familiar", names);
        Assert.Contains("Poporing", names);
        Assert.Contains("Red Plant", names);
    }

    [Fact]
    public void Database_map_search_uses_embedded_spawn_maps_when_gamedata_maps_are_empty()
    {
        var db = new GameDatabase();

        Assert.Contains("pay_dun00", db.SearchMaps("", 100000));
        Assert.Contains("pay_dun00", db.SearchMaps("pay_dun", 100000));
        Assert.True(db.Counts().maps >= 400);
    }

    [Fact]
    public void Live_scene_map_focus_keeps_in_map_names_and_generalizes_out_of_map_names()
    {
        var scene = new LiveScene();
        scene.Active = true;
        scene.SetMonsterFocus(new[] { "Familiar", "Zombie" });

        scene.SetEntities(new[]
        {
            new SceneItem(10, 10, 24, 24, "Familiar", 0.90f),
            new SceneItem(80, 10, 24, 24, "Evil Wraith", 0.90f),
        }, clientCoords: false);

        var labels = scene.Entities.Select(e => e.Label).ToArray();
        Assert.Contains("Familiar", labels);
        Assert.Contains("Monster", labels);
        Assert.DoesNotContain("Evil Wraith", labels);
        scene.Clear();
    }
}

public class ByteTrackLiteTests
{
    [Fact]
    public void Low_score_detection_updates_existing_track_without_creating_duplicate()
    {
        var tracker = new ByteTrackLite(trackThreshold: 0.35f, lowThreshold: 0.15f, matchThreshold: 0.25f, trackBuffer: 8);

        var first = tracker.Update(new[]
        {
            new SceneItem(100, 100, 40, 40, "Familiar", 0.88f),
        });
        Assert.Single(first);
        int id = first[0].TrackId;

        var second = tracker.Update(new[]
        {
            new SceneItem(104, 102, 40, 40, "Monster", 0.19f),
        });

        Assert.Single(second);
        Assert.Equal(id, second[0].TrackId);
        Assert.Equal("Familiar", second[0].Label);
        Assert.True(second[0].Hits >= 2);
        Assert.True(second[0].IsAttackable);
    }

    [Fact]
    public void Track_requires_consecutive_confirmation_before_attackable()
    {
        var tracker = new ByteTrackLite(trackThreshold: 0.35f, lowThreshold: 0.15f, matchThreshold: 0.25f, trackBuffer: 4, minHits: 2);

        var first = tracker.Update(new[]
        {
            new SceneItem(100, 100, 40, 40, "Familiar", 0.88f),
        }).Single();

        Assert.False(first.Confirmed);
        Assert.False(first.IsAttackable);

        var second = tracker.Update(new[]
        {
            new SceneItem(102, 101, 40, 40, "Familiar", 0.86f),
        }).Single();

        Assert.True(second.Confirmed);
        Assert.True(second.IsAttackable);
    }

    [Fact]
    public void Missing_frame_keeps_confirmed_track_briefly_and_marks_miss()
    {
        var tracker = new ByteTrackLite(trackThreshold: 0.35f, lowThreshold: 0.15f, matchThreshold: 0.25f, trackBuffer: 8);

        var first = tracker.Update(new[]
        {
            new SceneItem(250, 180, 32, 48, "Skeleton", 0.91f),
        });
        int id = first[0].TrackId;

        var held = tracker.Update(Array.Empty<SceneItem>());

        Assert.Single(held);
        Assert.Equal(id, held[0].TrackId);
        Assert.Equal("Skeleton", held[0].Label);
        Assert.Equal(1, held[0].Misses);
        Assert.Equal(SceneTrackState.LostGrace, held[0].State);
        Assert.False(held[0].IsAttackable);
    }

    [Fact]
    public void Missing_frame_holds_last_confirmed_box_without_velocity_prediction()
    {
        var tracker = new ByteTrackLite(trackThreshold: 0.35f, lowThreshold: 0.15f, matchThreshold: 0.25f, trackBuffer: 8);

        tracker.Update(new[]
        {
            new SceneItem(100, 100, 40, 40, "Familiar", 0.91f),
        });
        var moved = tracker.Update(new[]
        {
            new SceneItem(130, 100, 40, 40, "Familiar", 0.92f),
        }).Single();

        var held = tracker.Update(Array.Empty<SceneItem>()).Single();

        Assert.Equal(moved.X, held.X);
        Assert.Equal(moved.Y, held.Y);
        Assert.Equal(1, held.Misses);
        Assert.False(held.IsAttackable);
    }

    [Fact]
    public void Track_is_removed_after_short_lost_grace_window()
    {
        var tracker = new ByteTrackLite(trackThreshold: 0.35f, lowThreshold: 0.15f, matchThreshold: 0.25f, trackBuffer: 3, minHits: 2);

        tracker.Update(new[]
        {
            new SceneItem(250, 180, 32, 48, "Skeleton", 0.91f),
        });
        tracker.Update(new[]
        {
            new SceneItem(252, 181, 32, 48, "Skeleton", 0.90f),
        });

        Assert.Single(tracker.Update(Array.Empty<SceneItem>()));
        Assert.Single(tracker.Update(Array.Empty<SceneItem>()));
        Assert.Single(tracker.Update(Array.Empty<SceneItem>()));
        Assert.Empty(tracker.Update(Array.Empty<SceneItem>()));
    }

    [Fact]
    public void Low_score_detection_does_not_start_a_new_track()
    {
        var tracker = new ByteTrackLite(trackThreshold: 0.35f, lowThreshold: 0.15f, matchThreshold: 0.25f, trackBuffer: 8);

        var tracks = tracker.Update(new[]
        {
            new SceneItem(60, 70, 30, 30, "Monster", 0.20f),
        });

        Assert.Empty(tracks);
    }

    [Fact]
    public void Shared_scene_motion_keeps_tracks_pinned_after_large_scroll()
    {
        var tracker = new ByteTrackLite(trackThreshold: 0.35f, lowThreshold: 0.15f, matchThreshold: 0.25f, trackBuffer: 8);

        var first = tracker.Update(new[]
        {
            new SceneItem(100, 110, 32, 38, "Familiar", 0.90f),
            new SceneItem(260, 160, 38, 52, "Skeleton", 0.89f),
            new SceneItem(430, 220, 44, 42, "Poporing", 0.88f),
        }).ToArray();

        var ids = first.ToDictionary(e => e.Label, e => e.TrackId);

        var shifted = tracker.Update(new[]
        {
            new SceneItem(38, 149, 32, 38, "Familiar", 0.84f),
            new SceneItem(198, 199, 38, 52, "Skeleton", 0.83f),
            new SceneItem(368, 259, 44, 42, "Poporing", 0.82f),
        }).ToArray();

        Assert.Equal(ids["Familiar"], shifted.Single(e => e.Label == "Familiar").TrackId);
        Assert.Equal(ids["Skeleton"], shifted.Single(e => e.Label == "Skeleton").TrackId);
        Assert.Equal(ids["Poporing"], shifted.Single(e => e.Label == "Poporing").TrackId);
        Assert.All(shifted, e => Assert.Equal(0, e.Misses));
    }
}
