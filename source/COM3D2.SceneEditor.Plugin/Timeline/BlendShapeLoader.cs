using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class BlendShapeLoader
    {
        private static Dictionary<string, BlendShapeCacheData> blendShapeCacheMap = new Dictionary<string, BlendShapeCacheData>();

        /// <param name="reload">
        /// 中身が差し替わったモデル向け。既存コントローラも新しいメッシュで初期化し直し、
        /// モデルファイルが更新されている可能性があるためシェイプキーのキャッシュも読み直す
        /// </param>
        public static BlendShapeController LoadController(StudioModelStat model, bool reload = false)
        {
            if (model == null || model.transform == null || model.info == null)
            {
                return null;
            }

            var transform = model.transform;
            var go = transform.gameObject;

            var controller = go.GetComponent<BlendShapeController>();
            if (controller != null && !reload)
            {
                controller.model = model;
                return controller;
            }

            var meshRenderer = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var blendShapeCache = meshRenderer != null ? LoadCacheByMenu(model.info.fileName, reload) : null;
            if (blendShapeCache == null)
            {
                // 差し替えでシェイプキーを持たなくなった。破棄済みメッシュを握ったコントローラは外す
                if (controller != null)
                {
                    UnityEngine.Object.Destroy(controller);
                }
                return null;
            }

            if (controller == null)
            {
                controller = go.AddComponent<BlendShapeController>();
            }
            controller.Init(meshRenderer.sharedMesh, blendShapeCache);
            controller.model = model;
            return controller;
        }

        private static BlendShapeCacheData LoadCacheByMenu(string menuFileName, bool reload)
        {
            var menu = ModMenuLoader.Load(menuFileName);
            if (menu == null || string.IsNullOrEmpty(menu.modelFileName))
            {
                return null;
            }

            if (reload)
            {
                blendShapeCacheMap.Remove(menu.modelFileName);
            }
            return LoadCache(menu.modelFileName);
        }

        public static BlendShapeCacheData LoadCache(string modelFileName)
        {
            BlendShapeCacheData blendShapeCache;
            if (blendShapeCacheMap.TryGetValue(modelFileName, out blendShapeCache))
            {
                return blendShapeCache;
            }

            blendShapeCache = LoadCacheFromModel(modelFileName);
            if (blendShapeCache == null)
            {
                return null;
            }

            blendShapeCacheMap[modelFileName] = blendShapeCache;
            return blendShapeCache;
        }

        public static void ClearCache()
        {
            blendShapeCacheMap.Clear();
        }

        private static BlendShapeCacheData LoadCacheFromModel(string modelFileName)
        {
            BlendShapeCacheData blendShapeData = null;

            var buffer = BinaryLoader.ReadAFileBase(modelFileName);
            if (buffer == null)
            {
                return null;
            }

            using (var reader = new BinaryReader(new MemoryStream(buffer), Encoding.UTF8))
            {
                if (!(reader.ReadString() == "CM3D2_MESH"))
                {
                    MTEUtils.LogError(modelFileName + " is not a model file");
                }
                else
                {
                    int version = reader.ReadInt32();
                    string modelName = reader.ReadString();
                    string rootBoneName = reader.ReadString();
                    int boneCount = reader.ReadInt32();
                    try
                    {
                        for (int i = 0; i < boneCount; i++)
                        {
                            var boneName = reader.ReadString();
                            var flag = reader.ReadByte() != 0;
                        }
                        for (int i = 0; i < boneCount; i++)
                        {
                            var t = reader.ReadInt32();
                        }
                        for (int i = 0; i < boneCount; i++)
                        {
                            var localPosition = reader.ReadVector3();
                            var localRotation = reader.ReadQuaternion();
                            if (version >= 2001 && reader.ReadBoolean())
                            {
                                var t = reader.ReadVector3();
                            }
                        }

                        int vertexCount = reader.ReadInt32();
                        int subMeshCount = reader.ReadInt32();
                        int boneArrayCount = reader.ReadInt32();
                        for (int i = 0; i < boneArrayCount; i++)
                        {
                            var boneName = reader.ReadString();
                        }

                        for (int i = 0; i < boneArrayCount; i++)
                        {
                            var bindPose = reader.ReadMatrix4x4();
                        }

                        for (int i = 0; i < vertexCount; i++)
                        {
                            var vertex = reader.ReadVector3();
                            var normal = reader.ReadVector3();
                            var uvs = reader.ReadVector2();
                        }

                        int tangentCount = reader.ReadInt32();
                        if (tangentCount > 0)
                        {
                            for (int i = 0; i < tangentCount; i++)
                            {
                                var tangent = reader.ReadVector4();
                            }
                        }

                        for (int i = 0; i < vertexCount; i++)
                        {
                            var boneIndex0 = (int)reader.ReadUInt16();
                            var boneIndex1 = (int)reader.ReadUInt16();
                            var boneIndex2 = (int)reader.ReadUInt16();
                            var boneIndex3 = (int)reader.ReadUInt16();
                            var weight0 = reader.ReadSingle();
                            var weight1 = reader.ReadSingle();
                            var weight2 = reader.ReadSingle();
                            var weight3 = reader.ReadSingle();
                        }

                        for (int i = 0; i < subMeshCount; i++)
                        {
                            int trianglesCount = reader.ReadInt32();
                            for (int j = 0; j < trianglesCount; j++)
                            {
                                var triangle = (int)reader.ReadUInt16();
                            }
                        }

                        int materialCount = reader.ReadInt32();
                        for (int i = 0; i < materialCount; i++)
                        {
                            var material = ImportCM.ReadMaterial(reader, null, null);
                            UnityEngine.Object.Destroy(material);
                        }

                        blendShapeData = new BlendShapeCacheData();
                        blendShapeData.Load(reader);
                    }
                    catch (Exception ex)
                    {
                        MTEUtils.LogError(string.Concat(new string[]
                        {
                            "Could not load mesh for '",
                            modelFileName,
                            "' because ",
                            ex.Message,
                            "\n",
                            ex.StackTrace
                        }));

                        blendShapeData = null;
                    }
                }
            }
            return blendShapeData;
        }
    }
}