using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ModularTerrain
{
    /// <summary>
    /// 模块地形组件：描述一个矩形地形模块（tile）。
    ///
    /// 参数:
    ///   id (int) - 模块唯一标识。0 = 未分配；收集进管理器时自动分配正数。
    ///   description (string) - 模块描述（可选）。仅用于地形推荐时的可读性输出，不参与几何计算。
    ///   moduleSize (Vector2) - 模块的长宽（单位：米）。
    ///        x = 长（沿世界 X 方向），y = 宽（沿世界 Z 方向）。
    ///   heightZPlus / heightXPlus / heightZMinus / heightXMinus (float)
    ///        四个方向接连处（连接边）的局部高度，顺序为 (z+, x+, z-, x-)：
    ///
    /// 相邻拼接约定（供 Python 侧校验/推荐使用）：
    ///   模块以自身原点为底面中心（y=0），四周墙顶世界高度 = 布局高度(placement height) + 该边局部高度。
    ///   相邻两模块在共享边处，墙顶世界高度必须相等才算无缝拼接；
    ///   旋转（0/90/180/270，俯视顺时针）会改变「哪条局部边」落在哪个几何侧。
    ///          z+ = +Z 边, x+ = +X 边, z- = -Z 边, x- = -X 边。
    ///
    /// 几何约定：模块以自身 Transform 原点为底面中心（y=0 为底面），
    /// 四条侧边各自从 y=0 延伸到该边的高度。
    ///
    /// Gizmos：绘制一个「无盖无底」的盒子 —— 只绘制四条侧边的竖直墙面
    /// （底边在 y=0，顶边在该边高度），不绘制顶盖与底面。
    /// </summary>
    public class ModularTerrainModule : MonoBehaviour
    {
        [Header("模块标识")]
        [Tooltip("模块唯一 ID。0 表示未分配；收集到管理器时会自动分配正数（已分配最大值 +1 递增）。")]
        public int id = 0;

        [Tooltip("模块描述（可选）。用于地形推荐时的可读性输出，不参与几何计算。")]
        public string description = "";

        [Tooltip("模块长宽（米）。x = 长（世界 X 方向），y = 宽（世界 Z 方向）。")]
        public Vector2 moduleSize = new Vector2(10f, 10f);

        [Header("四边接连处局部高度（底 0 → 该边高度）")]
        [Tooltip("+Z 边（z+）接连处高度")]
        public float heightZPlus = 1f;
        [Tooltip("+X 边（x+）接连处高度")]
        public float heightXPlus = 1f;
        [Tooltip("-Z 边（z-）接连处高度")]
        public float heightZMinus = 1f;
        [Tooltip("-X 边（x-）接连处高度")]
        public float heightXMinus = 1f;

#if UNITY_EDITOR
        private static readonly Color WallColor = new Color(0.20f, 0.80f, 1.00f, 1f);
        private static readonly Color OutlineColor = new Color(0.10f, 0.55f, 0.80f, 1f);

        private void OnDrawGizmos()
        {
            // 用物体自身的变换矩阵，使盒子跟随移动/旋转/缩放
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = WallColor;

            float hl = moduleSize.x * 0.5f; // 半长 (X)
            float hw = moduleSize.y * 0.5f; // 半宽 (Z)

            // 四角（局部坐标，y=0）—— 顺序: (-X,-Z)(+X,-Z)(-X,+Z)(+X,+Z)
            Vector3 cXmZm = new Vector3(-hl, 0f, -hw);
            Vector3 cXpZm = new Vector3( hl, 0f, -hw);
            Vector3 cXmZp = new Vector3(-hl, 0f,  hw);
            Vector3 cXpZp = new Vector3( hl, 0f,  hw);

            // 四条侧边各自绘制一面无盖无底的竖直墙面
            DrawWall(cXmZp, cXpZp, heightZPlus);   // +Z 边 (z+)
            DrawWall(cXmZm, cXpZm, heightZMinus);  // -Z 边 (z-)
            DrawWall(cXpZm, cXpZp, heightXPlus);   // +X 边 (x+)
            DrawWall(cXmZm, cXmZp, heightXMinus);  // -X 边 (x-)
        }

        /// <summary>
        /// 绘制一面竖直墙面：底边在 y=0（勾勒底面轮廓但不绘制底面），
        /// 顶边在该边高度（不绘制顶盖），两端各一条竖直边。
        /// </summary>
        private static void DrawWall(Vector3 a, Vector3 b, float height)
        {
            Vector3 a0 = new Vector3(a.x, 0f, a.z);
            Vector3 b0 = new Vector3(b.x, 0f, b.z);
            Vector3 a1 = new Vector3(a.x, height, a.z);
            Vector3 b1 = new Vector3(b.x, height, b.z);

            Gizmos.DrawLine(a0, b0); // 底边
            Gizmos.DrawLine(a1, b1); // 顶边
            Gizmos.DrawLine(a0, a1); // 竖直边
            Gizmos.DrawLine(b0, b1); // 竖直边
        }
#endif
    }
}
