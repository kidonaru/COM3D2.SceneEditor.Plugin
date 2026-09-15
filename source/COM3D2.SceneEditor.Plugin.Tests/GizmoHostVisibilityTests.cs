using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// GizmoHost の表示状態 API は外部プラグイン (ModItemExplorer) がリフレクションで
    /// 呼ぶ公開契約なので、シグネチャと不在時の既定値を機械検証する
    /// </summary>
    public class GizmoHostVisibilityTests
    {
        [Fact]
        public void GizmoHostがIsGizmoVisibleを公開している()
        {
            var method = typeof(GizmoHost).GetMethod(
                "IsGizmoVisible", BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(method);
            Assert.Equal(typeof(bool), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.Single(parameters);
            Assert.Equal(typeof(Camera), parameters[0].ParameterType);
        }

        [Fact]
        public void プラグイン未起動時は表示扱いになる()
        {
            // テストホストでは SceneEditorPlugin.instance が null。
            // 表示状態が分からない場面では従来動作 (表示) に倒す
            Assert.True(GizmoHost.IsGizmoVisible(null));
        }

        [Fact]
        public void クライアント側が同名のメソッドを束縛している()
        {
            // GizmoHostClient は SceneEditor 側でコンパイルされない (ホストは自分のクライアントを
            // 持たない) ため、リフレクション先の名前とデリゲート型をソース突合で検証する。
            // リポジトリ内でのみ実行可能な開発用テスト (ソースツリー前提)
            var path = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\..\COM3D2.SceneEditor.Plugin\MTEUtils\GizmoHostClient.cs"));
            Assert.True(File.Exists(path), "GizmoHostClient.cs が見つかりません: " + path);

            var source = File.ReadAllText(path);
            Assert.Matches(new Regex(@"GetMethod\(\s*""IsGizmoVisible"""), source);
            Assert.Matches(new Regex(@"Func<Camera,\s*bool>"), source);
        }
    }
}
