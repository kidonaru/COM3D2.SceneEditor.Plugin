using System;
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>ゲスト側が自前定義する規約用属性（短名一致で判定されるため型の同一性は不要）</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ModelPlacerProviderAttribute : Attribute
    {
    }

    /// <summary>必須・任意メンバをすべて備えたプロバイダ</summary>
    [ModelPlacerProvider]
    public static class FullDummyProvider
    {
        public static string ModelPlacerId => "DummyPlacer";
        public static string ModelPlacerDisplayName => "ダミー配置";

        public static List<GameObject> GetModels() => new List<GameObject>();
        public static string GetModelFileName(GameObject obj) => "dummy.menu";
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible) => null;
        public static void DeleteModel(GameObject obj) { }
        public static void DeleteAllModels() { }
        public static void SetModelVisible(GameObject obj, bool visible) { }
        public static void AttachModel(GameObject obj, Maid maid, string attachPointName) { }

        public static string GetModelDisplayName(GameObject obj) => "ダミー";
        public static void BeginBatch() { }
        public static void EndBatch() { }
    }

    /// <summary>任意メンバを持たないプロバイダ</summary>
    [ModelPlacerProvider]
    public static class MinimalDummyProvider
    {
        public static string ModelPlacerId => "MinimalPlacer";
        public static string ModelPlacerDisplayName => "最小配置";

        public static List<GameObject> GetModels() => new List<GameObject>();
        public static string GetModelFileName(GameObject obj) => "";
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible) => null;
        public static void DeleteModel(GameObject obj) { }
        public static void DeleteAllModels() { }
        public static void SetModelVisible(GameObject obj, bool visible) { }
        public static void AttachModel(GameObject obj, Maid maid, string attachPointName) { }
    }

    /// <summary>必須メンバ GetModels を欠いたプロバイダ</summary>
    [ModelPlacerProvider]
    public static class BrokenDummyProvider
    {
        public static string ModelPlacerId => "BrokenPlacer";
        public static string ModelPlacerDisplayName => "壊れた配置";

        public static string GetModelFileName(GameObject obj) => "";
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible) => null;
        public static void DeleteModel(GameObject obj) { }
        public static void DeleteAllModels() { }
        public static void SetModelVisible(GameObject obj, bool visible) { }
        public static void AttachModel(GameObject obj, Maid maid, string attachPointName) { }
    }

    /// <summary>ID が空のプロバイダ</summary>
    [ModelPlacerProvider]
    public static class EmptyIdDummyProvider
    {
        public static string ModelPlacerId => "";
        public static string ModelPlacerDisplayName => "ID なし";

        public static List<GameObject> GetModels() => new List<GameObject>();
        public static string GetModelFileName(GameObject obj) => "";
        public static GameObject CreateModel(
            string type, string fileName, int myRoomId, long bgObjectId, int group, bool visible) => null;
        public static void DeleteModel(GameObject obj) { }
        public static void DeleteAllModels() { }
        public static void SetModelVisible(GameObject obj, bool visible) { }
        public static void AttachModel(GameObject obj, Maid maid, string attachPointName) { }
    }

    public class ModelPlacerProviderTests
    {
        [Fact]
        public void 必須と任意メンバを備えた型をバインドできる()
        {
            var bound = ModelPlacerProviderBinder.TryBind(
                typeof(FullDummyProvider), out var provider, out var error);

            Assert.True(bound, error);
            Assert.Equal("DummyPlacer", provider.id);
            Assert.Equal("ダミー配置", provider.displayName);
            Assert.NotNull(provider.getModels);
            Assert.NotNull(provider.createModel);
            Assert.NotNull(provider.attachModel);
            Assert.NotNull(provider.getModelDisplayName);
            Assert.NotNull(provider.beginBatch);
            Assert.NotNull(provider.endBatch);
        }

        [Fact]
        public void 任意メンバが無くてもバインドできる()
        {
            var bound = ModelPlacerProviderBinder.TryBind(
                typeof(MinimalDummyProvider), out var provider, out var error);

            Assert.True(bound, error);
            Assert.Null(provider.getModelDisplayName);
            Assert.Null(provider.beginBatch);
            Assert.Null(provider.endBatch);
        }

        [Fact]
        public void 必須メンバが欠けた型はバインドされず欠落名が分かる()
        {
            var bound = ModelPlacerProviderBinder.TryBind(
                typeof(BrokenDummyProvider), out var provider, out var error);

            Assert.False(bound);
            Assert.Null(provider);
            Assert.Contains("GetModels", error);
        }

        [Fact]
        public void IDが空の型はバインドされない()
        {
            var bound = ModelPlacerProviderBinder.TryBind(
                typeof(EmptyIdDummyProvider), out var provider, out var error);

            Assert.False(bound);
            Assert.Null(provider);
        }

        [Theory]
        // 旧タイムライン XML の pluginName はプロバイダ ID へ読み替える
        [InlineData("SceneEditor", "ModItemExplorer", "ModItemExplorer")]
        // それ以外はそのまま通す
        [InlineData("ModItemExplorer", "ModItemExplorer", "ModItemExplorer")]
        [InlineData("MultipleMaids", "ModItemExplorer", "MultipleMaids")]
        // 空・null は触らない
        [InlineData("", "ModItemExplorer", "")]
        [InlineData(null, "ModItemExplorer", null)]
        public void 旧プラグイン名を読み替えられる(string pluginName, string providerId, string expected)
        {
            Assert.Equal(expected, ModelPlacerProviderRegistry.MigratePluginName(pluginName, providerId));
        }
    }
}
