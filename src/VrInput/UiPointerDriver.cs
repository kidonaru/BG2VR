using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BG2VR.VrInput
{
    /// <summary>
    /// quad hit の RT pixel 座標を使い、ゲームの GraphicRaycaster（eventCamera=UI カメラ）に
    /// EventSystem.RaycastAll → ExecuteEvents で hover/click を注入する。click+hover のみ（scroll/drag は後続）。
    /// ゲームの InputSystemUIInputModule とは additive 共存（navigation 据え置き）。
    /// </summary>
    internal sealed class UiPointerDriver
    {
        private PointerEventData m_ped;
        private GameObject m_hovered;
        private GameObject m_pressed;
        private readonly List<RaycastResult> m_results = new List<RaycastResult>();

        // 戻り値: この JustPressed クリックが uGUI の down/click ハンドラを持つ要素（=uGUI が消費）に当たったか。
        // false（非インタラクティブ領域クリック）なら呼び出し側が会話送りへ回す。
        public bool Process(bool hit, Vector2 pixel, PointerButtonState.Edge btn)
        {
            EventSystem es = EventSystem.current;
            if (es == null) { ClearHover(); return false; }
            if (m_ped == null) m_ped = new PointerEventData(es);
            bool consumedInteractive = false;

            m_ped.Reset(); // used フラグのみクリア（press 状態 pointerPress/eligibleForClick は保持される）
            m_ped.button = PointerEventData.InputButton.Left;

            GameObject newHover = null;
            if (hit)
            {
                m_ped.position = pixel;
                m_results.Clear();
                es.RaycastAll(m_ped, m_results);
                if (m_results.Count > 0)
                {
                    m_ped.pointerCurrentRaycast = m_results[0];
                    newHover = m_results[0].gameObject;
                }
                else
                {
                    m_ped.pointerCurrentRaycast = default; // quad 内だが UI 要素なし
                }
            }
            else
            {
                m_ped.pointerCurrentRaycast = default; // quad 外
            }

            // hover 遷移（Unity PointerInputModule.HandlePointerExitAndEnter 同等＝祖先チェーンへ伝播。
            // press 中の凍結は入れない＝観測されてから対処。Task 10 観察項目）
            HandlePointerExitAndEnter(newHover);
            m_hovered = newHover;

            // down
            if (btn.JustPressed && m_hovered != null)
            {
                m_ped.pressPosition = m_ped.position;
                m_ped.pointerPressRaycast = m_ped.pointerCurrentRaycast;
                GameObject press = ExecuteEvents.ExecuteHierarchy(m_hovered, m_ped, ExecuteEvents.pointerDownHandler);
                if (press == null) press = ExecuteEvents.GetEventHandler<IPointerClickHandler>(m_hovered);
                consumedInteractive = (press != null); // この down がインタラクティブ要素に当たったか
                m_pressed = press;
                m_ped.pointerPress = press;
                m_ped.rawPointerPress = m_hovered;
                m_ped.eligibleForClick = true;
            }

            // up + click（press した click ハンドラ上で離したか＝Unity StandaloneInputModule と同じ判定）
            if (btn.JustReleased)
            {
                if (m_pressed != null)
                {
                    ExecuteEvents.Execute(m_pressed, m_ped, ExecuteEvents.pointerUpHandler);
                    GameObject upHandler = (m_hovered != null)
                        ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(m_hovered) : null;
                    if (m_ped.eligibleForClick && upHandler == m_pressed)
                        ExecuteEvents.Execute(m_pressed, m_ped, ExecuteEvents.pointerClickHandler);
                }
                m_pressed = null;
                m_ped.pointerPress = null;
                m_ped.rawPointerPress = null;
                m_ped.eligibleForClick = false;
            }

            return consumedInteractive;
        }

        /// <summary>
        /// hover/press を解除（miss / 非表示 / コントローラ切断 / config OFF / EventSystem 消失時）。
        /// 押下保持中なら pointerUp を撃って UI の押下状態固着を防ぐ（click は撃たない＝キャンセル扱い）。
        /// </summary>
        public void ClearHover()
        {
            if (m_ped != null)
            {
                if (m_pressed != null)
                {
                    ExecuteEvents.Execute(m_pressed, m_ped, ExecuteEvents.pointerUpHandler);
                    m_ped.pointerPress = null;
                    m_ped.rawPointerPress = null;
                    m_ped.eligibleForClick = false;
                }
                HandlePointerExitAndEnter(null); // 追跡中の hovered チェーンを全 exit
            }
            m_hovered = null;
            m_pressed = null;
        }

        /// <summary>
        /// Unity PointerInputModule.HandlePointerExitAndEnter（2017.x）のボディを忠実移植。
        /// ゲームのボタンは IPointerEnterHandler が graphic 無しの root にあり raycast は子 graphic に
        /// 当たるため、単発 Execute では root に enter が届かない（ConfirmDialog の m_select が更新されず
        /// 「いいえ」クリックが「はい」扱いになる実バグ）。祖先チェーンへ enter/exit を伝播し実マウスと揃える。
        /// 呼び出し側が m_ped != null を保証する。
        /// </summary>
        private void HandlePointerExitAndEnter(GameObject newEnterTarget)
        {
            // hover 先が無い、または旧 pointerEnter が消滅（destroy / SetActive(false) で fake-null）→
            // 壊れた Transform チェーンを辿らず hovered リスト経由で全 exit してから仕切り直す。
            if (newEnterTarget == null || m_ped.pointerEnter == null)
            {
                for (int i = 0; i < m_ped.hovered.Count; i++)
                    ExecuteEvents.Execute(m_ped.hovered[i], m_ped, ExecuteEvents.pointerExitHandler);
                m_ped.hovered.Clear();

                if (newEnterTarget == null)
                {
                    m_ped.pointerEnter = null;
                    return;
                }
            }

            // hover 先が変わらないなら何もしない（同一要素内の再 enter を抑止）。
            // canonical の `&& newEnterTarget` は冒頭ガード通過時点で非 null 保証のため省略。
            if (m_ped.pointerEnter == newEnterTarget) return;

            GameObject commonRoot = FindCommonRoot(m_ped.pointerEnter, newEnterTarget);

            // exit: 旧 hover の祖先を commonRoot 手前まで。
            if (m_ped.pointerEnter != null)
            {
                Transform t = m_ped.pointerEnter.transform;
                while (t != null)
                {
                    if (commonRoot != null && commonRoot.transform == t) break;
                    ExecuteEvents.Execute(t.gameObject, m_ped, ExecuteEvents.pointerExitHandler);
                    m_ped.hovered.Remove(t.gameObject);
                    t = t.parent;
                }
            }

            // enter: 新 hover の祖先を commonRoot 手前まで。
            m_ped.pointerEnter = newEnterTarget;
            Transform tEnter = newEnterTarget.transform;
            while (tEnter != null && tEnter.gameObject != commonRoot)
            {
                ExecuteEvents.Execute(tEnter.gameObject, m_ped, ExecuteEvents.pointerEnterHandler);
                m_ped.hovered.Add(tEnter.gameObject);
                tEnter = tEnter.parent;
            }
        }

        /// <summary>両者の Transform 祖先で最初に一致する GameObject（無ければ null）。</summary>
        private static GameObject FindCommonRoot(GameObject g1, GameObject g2)
        {
            if (g1 == null || g2 == null) return null;
            for (Transform t1 = g1.transform; t1 != null; t1 = t1.parent)
                for (Transform t2 = g2.transform; t2 != null; t2 = t2.parent)
                    if (t1 == t2) return t1.gameObject;
            return null;
        }
    }
}
