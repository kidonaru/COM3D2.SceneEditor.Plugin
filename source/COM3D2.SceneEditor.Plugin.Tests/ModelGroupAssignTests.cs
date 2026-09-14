using System.Collections.Generic;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// モデルのグループ採番を固定する。
    /// タイムライン XML はモデルを name ("ファイル名 (N)") で照合するため、
    /// 並び順が変わっても採番済みの番号が動いてはいけない
    /// </summary>
    public class ModelGroupAssignTests
    {
        private static MTEP.StudioModelStat CreateStat(string fileName, int group)
        {
            var info = new MTEP.OfficialObjectInfo
            {
                type = MTEP.StudioModelType.Mod,
                label = fileName,
                fileName = fileName,
            };
            return new MTEP.StudioModelStat(
                info, group, null, PhotoTransTargetObject.AttachPoint.Null, -1, null, "ModItemExplorer", true);
        }

        private static MTEP.StudioModelStat CreateUnassigned(string fileName)
        {
            return CreateStat(fileName, MTEP.StudioModelStat.UnassignedGroup);
        }

        [Fact]
        public void 未採番のstatに0と2から順に番号が振られる()
        {
            var a = CreateUnassigned("x.menu");
            var b = CreateUnassigned("x.menu");
            var c = CreateUnassigned("x.menu");
            var models = new List<MTEP.StudioModelStat> { a, b, c };

            MTEP.ModelHackManager.FixGroup(models);

            Assert.Equal("x.menu", a.name);
            Assert.Equal("x.menu (2)", b.name);
            Assert.Equal("x.menu (3)", c.name);
        }

        [Fact]
        public void 採番済みstatは並び順を変えても番号が変わらない()
        {
            var a = CreateUnassigned("x.menu");
            var b = CreateUnassigned("x.menu");
            var models = new List<MTEP.StudioModelStat> { a, b };
            MTEP.ModelHackManager.FixGroup(models);

            models.Reverse();
            MTEP.ModelHackManager.FixGroup(models);

            Assert.Equal("x.menu", a.name);
            Assert.Equal("x.menu (2)", b.name);
        }

        [Fact]
        public void 削除で空いた番号は次の生成で再利用される()
        {
            var a = CreateUnassigned("x.menu");
            var b = CreateUnassigned("x.menu");
            var models = new List<MTEP.StudioModelStat> { a, b };
            MTEP.ModelHackManager.FixGroup(models);

            // b を削除して新しいモデルを足す
            models.Remove(b);
            var c = CreateUnassigned("x.menu");
            models.Add(c);
            MTEP.ModelHackManager.FixGroup(models);

            Assert.Equal("x.menu", a.name);
            Assert.Equal("x.menu (2)", c.name);
        }

        [Fact]
        public void 別ファイルの番号は独立して振られる()
        {
            var a = CreateUnassigned("x.menu");
            var b = CreateUnassigned("y.menu");
            MTEP.ModelHackManager.FixGroup(new List<MTEP.StudioModelStat> { a, b });

            Assert.Equal("x.menu", a.name);
            Assert.Equal("y.menu", b.name);
        }

        [Fact]
        public void 名前から採番済みの番号はそのまま使われる()
        {
            // タイムライン読込で "x.menu (2)" から作られた stat を想定する
            var loaded = CreateStat("x.menu", 2);
            var existing = CreateUnassigned("x.menu");

            MTEP.ModelHackManager.FixGroup(new List<MTEP.StudioModelStat> { existing, loaded });

            Assert.Equal("x.menu (2)", loaded.name);
            Assert.Equal("x.menu", existing.name);
        }

        [Fact]
        public void 未採番statが採番済みstatより先に列挙されても既存の番号は動かない()
        {
            // プロバイダの列挙順で新規モデルが既存モデルより前に来るケース。
            // 新規 stat を未採番にしていないと (group 0 のままだと) 先に 0 を確保してしまい、
            // 既存の 0 番モデルが押し出されて名前が変わる
            var existing = CreateStat("x.menu", 0);
            var created = CreateUnassigned("x.menu");

            MTEP.ModelHackManager.FixGroup(new List<MTEP.StudioModelStat> { created, existing });

            Assert.Equal("x.menu", existing.name);
            Assert.Equal("x.menu (2)", created.name);
        }

        [Fact]
        public void 採番済み同士が衝突したら先着優先で後ろだけ振り直される()
        {
            var a = CreateStat("x.menu", 2);
            var b = CreateStat("x.menu", 2);

            MTEP.ModelHackManager.FixGroup(new List<MTEP.StudioModelStat> { a, b });

            Assert.NotEqual(a.name, b.name);
            Assert.Equal("x.menu (2)", a.name);
        }
    }
}
