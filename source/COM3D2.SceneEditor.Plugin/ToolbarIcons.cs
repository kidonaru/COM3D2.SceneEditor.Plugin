using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ツールバーや UI 部品で使うアイコンテクスチャ。
    /// 画像は assets/icons/*.svg を generate.js で 32x32 の PNG へラスタライズしたもの。
    /// 形を変えるときは SVG を直して再生成し、下の base64 を貼り替えること
    /// </summary>
    public static class ToolbarIcons
    {
        public enum Kind
        {
            // 背景表示 (山と太陽)
            Bg,
            // メイド表示 (人物シルエット)
            Maid,
            // ギズモ表示 (3 軸の移動矢印)
            Gizmo,
            // 平行投影 (投影面へ向かう平行な矢印)
            Ortho,
            // 切り替え (左右逆向きの矢印)
            Change,
            // XYZ連動 (鎖)
            Link,
            // シーンプリセット自動ロード指定 (家の輪郭線)。ON 状態は色を乗算して表す
            Home,
            // 選択対象へフォーカス (四隅のブラケットと中央の点)
            Focus,
            // ギズモ軸空間 Global (経線・緯線を持つ地球儀)。ON 状態は色を乗算して表す
            Global,
            // 編集モード (斜めの鉛筆)
            EditMode,
            // キーフレーム自動登録 (録画ボタンの輪の中にひし形)
            AutoKey,
            // モデル表示 (立方体)
            Model,
            // カメラ同期 (ビデオカメラ)
            Camera,
            // 視野角固定 (視野の扇形と南京錠)
            FovLock,
            // フォーカス固定 (照準の輪と南京錠)
            FocusLock,
            // ポストエフェクト同期 (大小のきらめき)
            PostEffect,
            // 先頭へ (左端の縦線と左向きの三角)
            SkipStart,
            // 前のキーフレームへ (左のひし形と左向きの三角)
            PrevKey,
            // 前のフレームへ (白線の左向き三角。塗りつぶしの再生と区別する)
            PrevFrame,
            // 再生 (右向きの三角)
            Play,
            // 停止 (縦長の四角 2 本)
            Pause,
            // 次のフレームへ (白線の右向き三角。塗りつぶしの再生と区別する)
            NextFrame,
            // 次のキーフレームへ (右向きの三角と右のひし形)
            NextKey,
            // 最終へ (右向きの三角と右端の縦線)
            SkipEnd,
            // 操作履歴を戻す (左へ折り返す矢印)
            Undo,
            // 操作履歴を進める (右へ折り返す矢印)
            Redo,
        }

        // 32x32 PNG (base64)。添字は Kind と対応させること
        private static readonly string[] PNG_BASE64 =
        {
            // Bg
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAACEElEQVR42u2WMWgiQRSGX72uYHOyjrAMKBamTGMhZG29bHlcyjQihIS7Ju4uV7hgI66i7e5V11qoQWyzthZ2FuncwIVco3dwubQTnlxgEd3kzLDN+cPAMg5vvvfPOPwAe+21QZRSesRZWPPFjWOxWMx1XbfX6zHHcbiOfr/P5vP5XFEUZSsAbm6aJms2m8y2ba4Da9ZqtRXEVtux81arxUzTNBXOwpqNRmPlxMbjwEVoFdIG2rSjXqzPEUAFgAsAOA4dgFL6Rdf1xWg0YpqmLSilRpgAqmEYC+YTwgBAMSyAC+zcr+FwyADgPCyAY03Tln6ASqWy5O5ANBrtBdwBA23HzhGGUnrJ9Q7E4/Fv+DshZBjgRPGv7UWu/wJRFD+Wy+V7tLbT6fwRBOFzmO9AJJ1Of/efr6qqdwBw6F+USCRuJEm64Q6QTCavJpMJWxch5O55jSzL17PZjOHAb24AkUjkVNf1Jdug6XS6ug+SJH3tdru/n+fxG+d4ALzLZrMeC5DjOA+2bT+sz7fbbbwnn94EIMuyi5buqlKp9EMUxQ87AQiCcGZZ1iN7owqFwi0AHPwTQC6XO0mlUj95pSFCyK98Pv8+EABDAoYFTC7VarWayWSaPAfWxEAyGAwYRr+Nj4XneR7GJlzIOxNalsXq9Tobj8fjwNcKIdAJ3pkQO8fNt3a/fhy8M+GrYvle/6WeAA90JR9No98FAAAAAElFTkSuQmCC",
            // Maid
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAB2ElEQVR42u2Uv4oaURSHf4HUNwnaeMG5d/xTLEsgTZgxb5AUuzNrYZPWJZUPoCh5hUAqS51OEJLnUBCLhaxs3IQtomwQTGBUxLOcZdMsBjM6kyLxg1MNc79z7/ndC+zZsx0PAJwkEon3XADcv2YWQrxQSl3WajW/3W4TV7Va9ZVSX4QQVuRy13Wvl8sl3WexWJDjONdCCDuyY+ddrpP/Yj6fk2maw7sRhc4JHzttoFKp+ACOQ7dz2Hjem2i1WiSlfPfvNcBXjdO+qYFyucwjOIokhRxCTvvv8H2ftNaXUV5Dy3Gc75z2dXL+JoR4HvVbYPMuOe08by4+dq31MHL5vaf4mMN2F7gj/C9kAbyRUn7QWp8ZhvGVyzTNMynlRwCnANJRiJ8opTzbtj97nkfdbpfG4zHNZrPb8I1GI+p0OtRsNsmyrKHWugngcVjyR+l0+qrf79Of0uv1KJVKXfG/u8oPc7ncBW2JbdsXAA62lT/MZDID2oHVakXZbHbAawW2x2Kxt57n/aQdaTQaP+LxeDVwA8lkcjCdTnf102QyIcMwPgX1P8vn8+cUEq7rngN4GqSBV6VSaRJWA8Vi8RuAl0EaeF0oFKher4dSvBavGXQMpyHXnj1ruQFkA3hHOVWKrgAAAABJRU5ErkJggg==",
            // Gizmo
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAACcklEQVR42u1XzYsSYRh/WTo5O6KDOoMzo2gzKJR4GL3oH6AQeNHLHnOoDgX7B3iYk4c+QPHon9BBDwbCnjI3Ask6lGSWH6i1mmWErFIH33hklUl22RVqlsAHXnh45v09v9/7Mc8zg9DW/mdjGOYljEsh1+l0+6lU6hgG+FrzO4PBYB+fGPgQ04ydZdnDZrO55MfgQ0zTrcdrptVROAVB+J7NZnG5XF6Rl0olDDF4psVR3IbBcVx/NBrh4XCIeZ7vLeOa3QNBED4uBYii2ND8NdwKuHQBDoejvRTgcDhampJbrdYnkiT9arVauNFoYJ/P99NkMj3WpgY7nS+q1eri/fd6vdjv9y/8SqUCdeD5PyW32Wyv6vX6qgBJknQcCARWVbFWq2G73f53uiNFUQ8Jgrir2va3/f6q/2CPxwMNyI4Quur1envLeKfTgb7wRlW671EUdX9jARzHHcbj8QFBELcsFsuH8Xi8IJjP59jlcg0QQibVdNrtdh8tRcDlpGn6PUEQd2RZHvA8/2xT/iscxy1WlU6nv3S73UXiyWQCq/sECzsFs8uybH86nS7mttttnMlkhuBDHCG0s4mA6+Fw+Ejd7eDGMwzz7hzcDqx8Npv90SlDoRDs2LVNBMQURZmqkyQSiW8kSd44D6jX6wOyLP9QYxVFmSGEohdmNxqNj/L5/HrLx9Fo9CtJkuGzcARB3IzFYqN1XC6Xw5DzwgJ4nn9aLBZxoVDAyWRyFolEPoui2ON5/jVFUQ/OgO2azeZ9mANzAQNYyAG5aJo+2ORr9wBursFgSCGE9hBCEkKI3OAIyRPMHuSAXJBz+x+xta2dZr8Bor/Sf2uEcZIAAAAASUVORK5CYII=",
            // Ortho
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAs0lEQVR42u2XwQ3AIAhFGcHROoKbyIa6EQ1Gm14aRLAnSIgXLL+f9iUA7MUFAGWcv8eVUqJSCvEJAHnhDtfQyGwVgIhIHLXWVRG9lnOIsDvQWtOIoBlfAt4WLaVShCigP8QSgr2ygPk2u8H3LQK2RjBd4+bWEag/QkVzdwHPb7jY/IwDLEIBIn8BShKeGYGGhO4OBAmDhEHCIGGQMEgYJAwSaknItcKYjpLQdTmd6zl6rOc3r44QOgBmBsoAAAAASUVORK5CYII=",
            // Change
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABYUlEQVR42u1X0Y2EIBSkBEuwBDqA/7si7OC2g6UD7QA68DrRDrQD6YC7IY8LMZgsLOFjbyd5MavxzSxvGJWxN14IHWNM0LE5OGPs4Jw7HOl3U3xP0+QArbXrug5ChpYCjFLKBSzL0lwEltwaY1Ii7s80FRn19UvmwiiAbdsc+ULnkuu+750Qoqjgg4DjOLJFcJDjxlpAL/R8dHd4AdbaagLQK0eAdzVukFIWVWxGkNMITIkJ5ak+yWwyUTeYMJ7/vu/F5Llp56/F5Ou6hm2oapAPaDbPs2+OrYb0i5MwDqKIfKhGjmAJANlpWf+iGPOvSX4/k4MAqZcYgaXx2FoPI42GcR7Q0jsy4lUafiTO86fJYbDSZKQM+OdJSMbsH05CjCAWAfOVJiP9++ww8iKQZgHjOAYT3i7S8KqKd4XC0iFYYjO2fu8bziISQdRGRHjKJaK43Xtf7bQr+fiQ72+wl8IPqIQOPusaykEAAAAASUVORK5CYII=",
            // Link
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAACrElEQVR42r1X0a2jQAx0CXRwlEAH8H8nhb+7v9BB6AA6gA5CB9ABdAAdQAehg30MstHCC7AE3llCUQjLjL2e8Ybo2rCIyCWiiIgS/nT5/o+GxYDKtm11u91UFEXjJ77jPhHlP0XEI6LW8zzVtq16F6/XS/m+DxIvIvKvBA+QXZIkyiTyPFeWZamrSIzgz+dzAuj7XsVxrFAN/OY4jgqCYLyvk+BKWGfLPgNvmkb2uxguh5+zB6AUWVdVNT3L25GfIVCEYTgD59KGa9XC71IJ9AQ35sdVyJDFAjzYWZNiOySwPVzJjwKl7TWJBSZrACoBMsPN+IzR/Caif0zGNCYCaFZUxXQhAFsxGtd1pey46uG3u8E7HKhj0Yi7FUA31ygd5IPm0aOu69HxeCvKnabK9cblNd6uzgGwFyDGTVWvkAgBKAl0Xbergm8mg0jTVCELlBJ7CAXoJNiAyncSRLUk+LnUGBxAnGHH+4YrgxJ0aWn6XgVHEvweyxicGy7mnmgZBJn+Gny9Ebvlzu7WwLMsE4Kesb0uTKYFCDJlHddMouIXd0wy2gAP1vTdojwb4KOO9cHCJDBYnizVUa7YriPgo03qGt2w1wrP6SREivCGx+PxrXFNwJH9dJjAy1mj4cqzDTLUSawF98SuVXsAlIDMeKRuEa6wZilFSaAoClFNo43m1YhlsmkatQ0PJAAYtwvrtOHUHRk0KTKRWOjYZCr+JaI/DOh9MuPjN3PatAKSdXtwKs574M2c3huTM5PhyZadOdPPjks7J5zH0mS4cT8mMJ7x9EYsy1JIJFpp0c0lSn7EXo9UoYd89MFyv9+lJ0ajgeHoZwITkzkSPrLWSWwFT7XLwCcSqAS2Y83pcJhgr+jOln1rOwqRGMjIvx3NaNL/9Y/XY4NJeTZ4VwN/ATB4H8FIkCRfAAAAAElFTkSuQmCC",
            // Home
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABs0lEQVR42tVXi5GDIBDdEizBEuzgKCEl2AGWYAfagXagHaQE7UQ68O4xi0OIBkE9JzuzMwT28xYerCH6cklZb5GKiGbW6j8TJ0T0zLJsnqZJK8aY47VLJSOiIc9zndgIxpjDGttclnySUi6Jx3HUagRrsLkCRI6zbppmSdb3/ZwkiVaMjcCGeZGfRjYkGYZhSVLXNRIoInqwKswZgS18jpIThGpAMDs5n7VythljhTUbBJOziSEnHAYhxEI2pdSM338VjxsBMTfCBraGnOwzhIDQZLOrAdG4mtYTCGstbG1y8q7tIifOc6qq6iU5n2cdsIM1fGwQiMkgHltOEk5d1y1ObdseYbS+OYhhBLG5GOkaNy7Ti6JYI1vM26EQa+WG1HZDeXnZDIG4+vRgs/LG1kaGubawkTgAAL5vcZHLLa60OttlAOwcnPNtu4odAGD3s6HpDgDFp2MVHgClU8WalgHxggCsEspD3FMBrBLKQ9zrALhbfxeAwkPcywGISJ/vAiD5jsuNNRnoI2JfrjmAhHPsy7rrqsXKHgCbjemoWA3I+2m257mN1TKkj4uT9bY/sR/lF/JXOcaDre15AAAAAElFTkSuQmCC",
            // Focus
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABEElEQVR42u1XgQ3DIAzLCZzACf1gfNSewGftR/ABmyU2oW6QwKCrplqKhAIkbiA0IToZNBHdCqJH2rBEFARiC86bbYBVcM6FEjAfDejMlzfbMFBy8N4/N6scAazhEG2YLIFRR1BLYIn6VKSXcL9vaSFgOmaXuQicnsArhZhU2wMv2yS8mKx9K0w1YH6I01oHCMZRR8IUtSWmXKotSqmwrusrpBhDF9ONi4T+9jxd6jwlESMxFAYhzyFGYfprAoDftu3NOXSY61GQiC5hSgLjHpewJg3hyMNpdOwFzq2oIGGKjj2mmoeoZP/6F1wEPi2YO/YFcwuB3xWlI/sCtiAZ3RdIC5LDW7NSTd+jL6i1cRzutkJBgTKhrEYAAAAASUVORK5CYII=",
            // Global
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAACW0lEQVR42u1XO27CQBCdI/gIPoJvEN8A+hT4ELQguqSLq7T4BvgGbB8kb5/CligosRSl3vDQzGpZLcg2kERRnrQCz29nZ2bHY6J/9MMDEb0Q0RsRfRKR4dUyDbzkHhtPDqsejUZmuVya7XZrfIAGHmQgyzpXA6epJpOJqevadAVkoQNdIoqHbp5GUdSu1+sT423bHn93u52Zz+fHhf8uT7BarQxswFbvzZMkMVVVWWNFUZjpdGqfwSeiR6w0TS0dMpAVwAbLdnYigdeyOU41Ho9h4ENoeZ7juXB0CtkUMpCFjkQENI5Ep3RUEnYYYO+f4zj2T+8ai0ETQBY6oIkTSAfXxEVkWZZZQ3zyhUtvmgY0HdDV4AGQPTyPiSiHDQHTs0sO1FLtCKmzUVGWpRv+PKCbSxog66RICx22+YqGC8/1lsMoTUV5UUkv6e/3e8goqSk3fawfbFb+CUqHZw2g4s+ewLkNRLR3WKVEkCMbiiApCT/naiGMKIqsYfw/54Arxy1asJAa4jSokP7OO6Vd3skuro423kMOmO+CF53f48CPp0B5jeQuRciNTH3HNWz7XsObNSJ2oHcjAhpJA3ta3aAVV0Ln8DdXvYy4kYTeapXXyAa9jI4vD6WU7en86n0a8Dp+BQ02nKjoLvNAjOFBa22dkIFEaJye4EACGRlIZHPQ+gwkdiSTDQFMvF1HMsgKYMMr6H5DqaQjNJTOZrPjOjeUIuxDh1J3LNcoHrkdXQBZLjh9zVh+cjtwfZBX5Prchwl4XC9Nl2ofGhH0gA13OvfTbMO8u3ya/V18AQWIeXCaNPIFAAAAAElFTkSuQmCC",
            // EditMode
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAA10lEQVR42mNgGAWjYDgCCQmJXQNmuYiIyP3/////V1RUfDNglsMAXR3By8v75j8WQBdHwHxuYmLyn+6OQA92XI5QVlY+SnPLcTmCJiGAy3J0RwyI5TAgIiLyecAsH1Cfj1o+ajlVgLCw8M0Bs5yBgUHf29v7Oa5ilR5lfNykSZP+D1gFIyYmtvjgwYP0LduRgZyc3K3379/Tr2xHA/JWVlbPQJaBHAEKic7Ozm+gNCEpKfmAHo0bPyMjo/cmJiYPQCEBig5QmgAlTHo27/xAITHavh8F9AAApTpv2SiVWGIAAAAASUVORK5CYII=",
            // AutoKey
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAACQ0lEQVR42tVX0Y2DMAz1CP3oAHQDNigbtBuUDY4Njk1gg7JBskG7AQxwUrJBrg/ZKOWAhkKvd5YiVcF+fnWeiSH65xbz+hWLiOiDiBQRuZGl2CdaM/GGiAokOBwOrigKV9e16xv28Aw+TKbg2EWW3v6ROZ1Og0nHDL6IQSxjPGWfm83GKaXuwKuqcmmauiRJXBzH7cJv7OGZb+fz2QEDWHOT5wC+XC4dWFmWLooigDU34VXwIaKEV857DXzgKwYMYLFPWNnBWpJba93xeASAva0sIB4+FjGIFRJciTREcEbKDgBmr2cqG74asUICx8GamBRmibMU43+ux5y322292+2+JvA0MMSAjRxTrDu14xy57NFYcgGeIIFYK5oANrfoIGbms2XBZY+SB5DIgNWr6iCuFqZoJ1Z7UPIAEo20KFe2GnLqys9nVc1J/oBEJdryjuEnATG8WPp9G5J8gkQOTLEhArFPgFsveSb5CIkEmD0Cd7do8m4Cbz+ClkDTNC8XIXKMEei3Yb1iG9Yhbfj2F1HkHwMzNSu8io1U1it/NOcyUgsuIzXnMpLr2Gqt2wBjjLSkeuI6VogFhqcrGzIntgPJ9XrtSHAlDE+8jww+BjGSHFihA8ndSCYkYJh4WZjojjPPeXten7xXwwe+YsDguJxmWg7Wchz+oImJd7/fd0MpfmMPz/oDLP/z2cn9sdxCPNIdIQZfFpxdMpb7woRyWy2gpYbIYA/PWC+i9sUfJn1lZzwjjn2aafZZ9dPsz3ycvsS+AZzysnurqMBiAAAAAElFTkSuQmCC",
            // Model
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABhElEQVR42u1W0a2DMAz0CIzACG8ENoENYIOwCWwCG7xuABuQDfI4ZFBKG+KkUL0PTrJkJY59QjY+ohvxUEQ0salvFs5nG8qyNNM0LQYfZ3x3GTIi+s3z3AzDYPbAGe4Qw7Gn4YeIuizLTNd1W0GttanrejH4KxCDWLzht9FIiahJ0/SpMNC2rcH5HFTD4OPMBt5wTMO5xEjQVHjcNM1T0r7v16TtLin8FneIsYEc/EZx7kMUSZJopdTSXHZh/qy957PirkesTQS5kBO5UeOIwGQXHsfRFEWBwo/AxkLsA2+RwybCY+vEWZ3tnBjUEBGI7GznxAQTCOxs78QEE+BASWdLJiaagLezhRPzEYHDzhZOzCkEtn/FbGNVVVssfJwdzPipBC6NvQn8HwIClSNJuqknKQFt/zY9u8A3si+7ALlRw7uOhSrH9dNyqifJOl4FiUTl7AlI1FMSooy8KsdeXAHqKUqUvt0F7/wA9RQly2N3wanYdgGaCybYBZeg5pHS7N+Iwh9+QTDtyLGrgQAAAABJRU5ErkJggg==",
            // Camera
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABUElEQVR42u2WTWqDQBiGv1ULbsSVivh/hOoR2m2SA7hMoXoA/0AXruoNFEIvoFeoh2gOELJVIatsLROSQls1/hTSxTzwwYDo+8yMzicABoPB/GYFAOuJtZyVLMvym+d5dZIkzZTyfb+WZXnTEyHSNP0OAA+tM/c8r2pm4jhO3bISdxRFvWqatrNtuwGA5zaBNZrFXNI0/RZAEMQLz/P7LMtar3cKFEXRSJJ0KjSeIPDIcdxHHMfHPsFOARQMAKdC4zECLMtuTdM8lGV5dYU6BUiS/BIQRXGwgGEYTRAEg7eodwtQMKoxW4AE+t6lwQJT+RcCYRjeTgAFMAyztSzrUFXVbQTOAU+zPsM/PIhMQRD2eZ5fFVhFUXScK+C6LjqKFz+efU9RVKzr+s5xnE4BUBRlg5oRspxS6F5VVdOeZiT1NaMLy7PhlFrgvxkMBjOWT3MPfOKWmWBnAAAAAElFTkSuQmCC",
            // FovLock
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAACMklEQVR42u1XsW2DUBC9ETwCIzBACm9gl+lgA7MBbBA2MKUlF7h1BZILl3iASCBlAOgj+ccP3aFvDAYMtps86aToc/n3uHvvSIj+8Rx8cLwcBhF9E5HiSF5VeEZEXyi6Wq3U+XwuYzabgcTns4u7RJQvl0uVpqnS4fs+CPwwwclhXSKdz+cqiqKrwpvNpvoZzy/J3pSF50QUGYah1uv1VeEgCBTOieg3juPyDF1Bh6boAgQWYq6u614VRjF+0+wSNgJEBKZpKj4fL7A8z6uLsyxTmD0RFQ1tzqQL6MyjjmgUWFEUynEcsZrf0l7ftu0yH6Q51xgtMM/zxF67jgsN5IEsoi+BPgKLOa8PttpS2nYl+z0F1tcpKQgvFosymHzaRh6tGSKwe7DxIvUOAjjj8dmNBDArAUTHbw4PrwZYNRfd7Ha7UjOI0+lUnuEZ33mjhwDskKwDv8A+TnvMPoA7RKw8e4g1QCdBCIA7+KxxdifMS5Ib2hddLjVbCCTifS6+rI9GdNW1FzCjDCOQC8XPECkTWTf4v9IRE6gvNH09qz4bEOIr0DKIUteHZVkyS1cjUuW0FOh63iqsSh+6UKEPFir0ETYUuIna80FfScw9BhEsJR1hGFYFDoeDOh6P5aiaIkkStd/vdVKDAWFlcIauD1wmu78P2AlqzOfZgT4wAugDl9UtfA+aTUf/LehLO99B4GqTvpMA/RN4hwtuCAgJ+QK2hVZ8UgJe2/a7E5P+v/AQ/gD8MRpX03WLWwAAAABJRU5ErkJggg==",
            // FocusLock
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAClUlEQVR42u1XO47iQBCtkNBHwHABboBvABOS+QbjBAmMBJY4AI53GZkb2CkRSASEIDgAPsAgO5q0d16ra9TrsY1hWEi2pBJW16df168bouvIJiKh2KYnkNjtdpIViMcDYHokAIOIXokoyAEQKJnxrza3iOhkWZaYTCZitVp9AcA31iCDjtK966lnhmGIMAzFJYIOdGFzr2j4rVZLnE4nuUGapsL3fXna4/EoGd9YgwwEXdjA9sdhx2l48/V6Ler1OhxHRDTX2hDfEWTQYRAqEtZPQn/isMMxEaVE1C2xgSxlELBVNXFTKhyElsOuTt6tMqCgy+lQhencAmDheZ50gvyqsGfppQBUBBsQfMDXLeP1K5/qFHNd0TTNwHXd82g0Opum+ZbxM+foqdSJqmNbjtYsHQ6H7LR7cV33neWDweCciYS00QlA+v0++2kXAtjv998AoNWyAKbT6QfLh8MhAHR0P7DhIYWaAHc6HcmqnnIHVuUUNBqNN6QA3Gw2f+WlIAgC2Yr4zRLLylKSLcKwoO06OevheDyWG/DIjqJIFiSYowzZ5/2RfO5VL23DJEmubsNeryccx9E7gTsJHZECEMi27cIuwfCIWVFD273QRQl0MYo5hWpz3c5GdLQu2ZWO4jiO/yoolY7fWr3gO4SMQ451RK7gvWDwdY6Rfek9IS8jBgGns9lMtNtt2WpgfGONN4RuhQdL5QcN0PqIBKejjBaLhajVankAvnFG7lV5kMQoTBQV55fziDXVrjFvsNlsxHa7lY+VPMbQWy6XOqhKN6SjqjZ7goWSyfyiuquS6oSr35VlORQ8Q6qQ1qbXAcBAAT8LQNkfk4cAKI3OfwCP6IJSAAyCb8Ai1ja/KwCvaPqVsEfPpj86J6oiwOPdqQAAAABJRU5ErkJggg==",
            // PostEffect
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAB2ElEQVR42u1WzWrCQBDe44DePAnxpiy7xXfw7gP0LIjRd/Ga/jxC9VA2gQrqC2i89aj2UArtWaXXlK8kxQZrks1PLw4MDNlM5u+bL8vYRXKUUqk0gf5X/Csp5VJK6TLGZOHRiejecZy9UmpPRDeFVk5Ed4PBYOP50uv1tkR0i7NUXy6Xywp6buZoOyr3QmLb9l4IsUyFCcMwNrVabR2RgGvb9iGcgFLqIIRw0yQgWq3WKxT2mfckZm6a5jYI3u12X4jIivCLlI5lWd5wOPyEHQOEt47jHFB5JiDknI9Xq5UH5ZyPYq6hi7anrRzguzZN8z1oKWzGWDsmET2lJrR6vf4WBhWe4Sz3pa5Wq5PFYhGO7+EZznKlcgSYTqc77w/BmZ9Etp3AzNHiU5Wf6oQ/jnbauEBqh3P+cAy4uAIffzs6iVAPajUMYw2CwZ5jzXQFvuAJfAuMeY62fyWAl7NOALQdK4ETIxj3+/2PpMHhozWCIkAIMtIhpKzWsNFsNp+hsIsmokalUnmczWY7KGydJLSoGC1H1Qgc+MDGs8TjACaOgQk7iniCBObz+c/tCLZWAv7veHT0Ox7HdPseAQJDdUegdSHJDIQaV7LM1jDxpTR3ibqWX0RHvgCu599ZtDBdAwAAAABJRU5ErkJggg==",
            // SkipStart
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABKUlEQVR42u2W4Q2EIAyFu4GMwAa6gYxwm+gGuIFs4gi6wbmBI+AGXHoRj0PEu4j95UsaEhLbj/aZFODWrY84AJSB4BfU4aFLDQAmEDohhFzybXKKneI2xMnC+P0zlnMFaNvW9H3/PhMAMABof3nUCoDFUXieBHi4Y82yzFRVRQKAs+3d12JhrXU0ZyoA6b46z/M131HOswBfJsN2SymNrysANiYry9JM02RCSg2wMVnXdSamVAC7JjtSCgAeMxkFAM58tvdFUZAD2C4M7gjquiYbgW/CtRuMMTIT+r+hcrshhCD7Df3vR7cbTdOQAlg1MZNSAERNSgWwa1IEOQRQSplhGN5nooVE/bWQXLiSjbGc3G2XF/PykhSyJp33NmMRiCvWcga3bi16AURINsreyB58AAAAAElFTkSuQmCC",
            // PrevKey
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABZElEQVR42u2X0XGEIBCG6UA7kA6kA33Oi7zlNR1oB0kHuQ5SgunAdGA6MB1oBUdub9ybPQ4U8AYzk/tn9kUZ/m9hXZCxh/6h+By7qD7FOAePnXXHGFMkyljmL3PGKjZAyhhrqWlVVdEAJM06z3PV973qus4LIKRaIesPmnVd1wrlAyBItQpHc5hwQIMsy86GVK4AQiuaNQjI+l3PehxHpcsF4GKeJMk5ViDgWY+Twvi2bZVNawBX5lA0EAsQr3qFm7J2BbgxRxkgnn2ydgHgNnMLxBEnKYpCDcOgXLUEMMHDNE2tAPBOByjL8i4AuAVGCM0cxjydGs03TgTvtm6BFcJgTovwjRahlHJTERohFszpeOfV8GlEExk4OXTDA12NpmmCG5EOMXm24h804JwHt+Kth9HNaoQCbD2OL9sohAg6ju9xIfnUv5Q9rmRSK+roAFhPX3sCoBryde32b8Dn+njob+gXuIDefZjDl0IAAAAASUVORK5CYII=",
            // PrevFrame
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABWElEQVR42u2X4Y2DMAyFPQIjMAojsMHdBrABbNBuABu0G5BNkg3CBj4ecqS0pQ1qMa1OfZL/IJT3cL5Ygeirf65c6i3GJyJiqXYv44yIDjCtqoq993NJCPVONETky7Jkay3HkgCFlvHPVLYoCh6GgZekFQALDnmec9d1F4Z93zOeawWYAcuyjJumuTA2xjA6QUQjTLcOcANYkHOOsfdi3IrZpgEWARvHkeu6DgZHCUlbBrgLWNu2jG0govPCEXs5QBKw6R3zYNGXAhwTgLmpfld8wFMB0MoUYGs7+HwAwBUE6CSAFxhVA0A9tgCQxQKEsgVW4FQLEBYwgA3QxQKUAuGgBWEswObw5YAwFiCVY3jSOIbXAnwjWACUQYAVU1EMDhqD6HoUY9rN0y8BqkqA+KScE6B6zQCrQd3rQnIX1L0CPAR1zwCLoKL2upTegPqOa/nH/Jh8tVp/IUCFLi4dEEYAAAAASUVORK5CYII=",
            // Play
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABJklEQVR42u2X0Q2DIBCG2UA2kA10A91ANygb6AZ0A9xAN2g3sBvQDewGdgPaSyChFx6aNBxp4p/cizzcx/13gIwd+gMJF9mS7y5UDoD2ndgGsblv2QB8XBhjnBRgGAZblmUIAbYMZADrutp9361SClfDpLTlA8DLGGObpsEgOoUtUQCveZ5tURTYlhMZAAhsgf5A1VgZYzUJgBesV1WFQdSvtnwN4KW1xrbA2dGTAXhbuq6L2SJIAEJbImcHJwUQQoQATxKAbdts3/fYghuJBdM0Wc55mPhB0oSwXtc13vU5+RhCx0spY+VOfxAty4LLDU0mSS6jtm3xrqfklxGUexxHnPhOch1D4shMj7meZFfyJ1kw06SPUuFK/XQznUU854/JoeR6Ac23lFmtek9gAAAAAElFTkSuQmCC",
            // Pause
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAhklEQVR42u2W0QnAIAxEb4SO4CZ1Q91IN7IbWCL4U9Ck6EcL9+Ag4BHfZwBCxjgA5yBuQ1/9vACog5TH0rd9FT9Z1uMX+naBEEJNKbXIrAkY+3YBWdSRWRMw9ilAAQpQgAIUoMCPBGKMNefcIrMmYOx//ySTA/KaLJO3Y6FvlvCDuA19Qho3V5Pn/76EO/sAAAAASUVORK5CYII=",
            // NextFrame
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABTklEQVR42u2X7XGEIBCGtwRLoBRKsIOkA+1AO4gdaAdnB9IJdAAdEF9nzXAz9+ElLObHvTP7Rxl9lIcVid7551Fcp6Qnosh1KQ2Cm0Xv/VZN0+wgX0RUlQDQuGEaa22s6xoQfj3XFQfYsyxL1FoDxK71UQRAKRWnaboCGcdxO05EC4+XA+D5D3hyY8wVSNd1saqq7KLeAtC8MgJccM79nJcQ9R4A8cUHHGvbNoYQRER9BJAu1Rmvv+/77KIeAUjHmtyivgKw53Mt90TUQRIgbeE3ReXrKGkAyOcBACn3QFZpAMhmMQWQMA0k5SmYpCRcIBukSwMpWUIjISFe5QVPBsnSQEJeho7lzN6I0O227ge59kA6bkSBZczeiO4Khq7I4wapVuyfCDaLfYxyC/anDUkOwX4FkFOww5tSyCUh2NEMybZ8Puv/4NQfk3eK5Budh4U+NFJuvQAAAABJRU5ErkJggg==",
            // NextKey
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABXUlEQVR42u2X0W2EMAyGswHZgGxANoDnvsAIbAAbtBu0GzAC3YBuQDegG5AJmpOlcPKlziWkatKH+yU/IAL/Z2MHYOyhfyRhIpv5bmLIAdAwxjSKJXU1bABtqtElB2jb1gaZGWM8GcCyLHpdV11VVdJq3AAcGobBrsZrRDWCposEAMFxWZYYYjPrQyTRdMkoANC+765qhJjjxyijAA7N86yLosAQq+OmV3NYj65xQgQBHNUgJuXZZQ4NDeGDCAbwVOPJNj/kgzgNANq2Tdd1jSG+KfM7EOLXAE3T/ADgnDsB4JxZq/BInwaYpgnfDOLTPAJFQRDmMroJu66zm/DFasIbCJ/5qTEksnaN4RXCZx60EfV9b2f9FrARKbReRW1EcCyEwMZfJ7di5TMnASDrcRyprP/+ZQRNI6XUVvnSvI6JDn9P+kGSMut7AB+pP0oF6tYx178Bz/lj8lA2XQDZFd2gBKfvgwAAAABJRU5ErkJggg==",
            // SkipEnd
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABG0lEQVR42u2WgQ2EIAxF2UBGcAPdQEZwE90ANtBNHEE3ODdwBNyAS43mLg3GckKTS/xJgzGEPtqPIsSjRx/le8Res/JE7pto99ARk8N6zhMWQyg04bW/uyN1kvwIRZncCSHkXYCu69w4jtt4CdA0jcuyDJervgMAyUEwXgLAJGvtBoKqMQaa9HeAQ/BcFAWuhmYDOKS1xm2hmDQeAGhZFldVVYhJ4wIcGoaBatI0ACCiSdMBXJhUsgKUZfm96MoCAC1o2xa3YGJpAZhQSol3XbMcQ6UU3nXPcgyNMXjXM8uH6MRkhuVnRDBZGgBITDRZGoAAk5EB+r530zRtIxVg5r6S5XupQ0xGuZSuJ8lXX2Vlomu58kTsPI/+WG/d5Ta61QkDrQAAAABJRU5ErkJggg==",
            // Undo
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABVUlEQVR42u1X0a2DMAz0CIzACGwAI7ABbPC6QdkANggbwAbtBmQTskEe92QjVQIVEx58tCdZVaWmdz47NhB9oUPMcQlqIvIc1dnkJkkSP47jX7CI+HRywDknAqJLyPF9Im+uJG8/lnwgovRNBDdms0KuiXES85g+f/YMmZl8L4Zh8MYYn2WZuJaoBCDro1DXtY+iCEJKVQlEBNzQlAC/LcvS933/4giL2OxEuyICdmYbAmPa5Xk+/0fTNHKeQkWYjecxJZ9wQ8A9cTtbhJNytG2Ls0/tzVgTsXUUV+ICeoGvKAWLUGzDDNYLQpbYLEK5DQ8TQGy79oFkLoG1FufcmY9kyHTsui6oCfcC5I/Qa7jFjaWNeEfmIJe9woPIHkleLY3iNE19URRebJfaa0fxYVsTmWuXUfDWRMZoOK65PTLztyXgcNztt//u9HhhE1721vTFbvwCXmmMUauJiiwAAAAASUVORK5CYII=",
            // Redo
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAABW0lEQVR42u1XwZGEIBAkBELYEMzgCMEMNIO7DM4MJAPMQDPYzQAygQy4bWuwfKwrCiWP266a8oXd0zPOIGMf5MWNogg6xpin6Etk7q21c1RVBRHqcgHOOQ+UEiFBWlrE8C9EoN5fO6FziuCMse/ni+/Pp119aruxIUIeIa+QjRDCK6W81tqnACJIXNSwajnnvu97nwtwgwTw3cxBvs54mibftm2wMboEyDqQx5YA6rSUcjlY1zUOOhq3IiL0BvkQY/0Pah5A5I8Y2wgqhRx4DMOw2E6ZX0YO2FB71Jxsjx7FqeTzUgmgF4gj2zCFPFkAiFPIA5wx5kwJ1hcSmTLrlyYcx9HT+OUHHEy+kr36DO8HRCQDRCYMIjQWiYATvxvbL/tFdB7FoRdCOZqmeTeKu9wi5mUUnMi55Y6uY4OeQGOuHTm75U43Ju0D92b7dVc16e3F9iv2N/RBMv4ARd+LhHDwnncAAAAASUVORK5CYII=",
        };

        private static readonly Texture2D[] _textures = new Texture2D[PNG_BASE64.Length];
        // 毎フレーム呼ばれるため、失敗も記録して再デコード・再ログを 1 回に抑える
        private static readonly bool[] _failed = new bool[PNG_BASE64.Length];

        /// <summary>アイコンテクスチャを取得する。読み込めなければ null</summary>
        public static Texture2D GetTexture(Kind kind)
        {
            var index = (int)kind;
            if (_textures[index] == null && !_failed[index])
            {
                _textures[index] = CreateTexture(PNG_BASE64[index]);
                _failed[index] = _textures[index] == null;
            }
            return _textures[index];
        }

        /// <summary>
        /// アイコンに色を乗算したテクスチャを生成する。読み込めなければ null。
        /// 呼び出しごとに新しいテクスチャを作るため、結果は呼び出し側でキャッシュすること
        /// </summary>
        public static Texture2D CreateTintedTexture(Kind kind, Color tint)
        {
            var source = GetTexture(kind);
            if (source == null)
            {
                return null;
            }

            // LoadImage で作ったテクスチャは読み取り可能なので、そのまま画素を取り出せる
            var pixels = source.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] *= tint;
            }

            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D CreateTexture(string base64)
        {
            try
            {
                return CreateTextureFromPng(Convert.FromBase64String(base64), "ツールバーアイコン");
            }
            catch (Exception e)
            {
                MTEUtils.LogError("ツールバーアイコンの base64 のデコードに失敗しました");
                MTEUtils.LogException(e);
                return null;
            }
        }

        /// <summary>
        /// PNG バイト列からテクスチャを生成する。読み込めなければ null。
        /// errorLabel はログに出す対象名 (「〜の画像を〜」の形で埋め込む)
        /// </summary>
        public static Texture2D CreateTextureFromPng(byte[] png, string errorLabel)
        {
            // 幅・高さと形式は LoadImage が PNG に合わせて作り直す
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            try
            {
                if (texture.LoadImage(png))
                {
                    return texture;
                }
                MTEUtils.LogError("{0}の画像を読み込めませんでした", errorLabel);
            }
            catch (Exception e)
            {
                // 読み込めなくても機能自体は動くので、呼び出し側でフォールバックする
                MTEUtils.LogError("{0}の画像の展開に失敗しました", errorLabel);
                MTEUtils.LogException(e);
            }

            UnityEngine.Object.Destroy(texture);
            return null;
        }
    }
}
