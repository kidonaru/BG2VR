namespace BG2VR.VrInput
{
    /// <summary>bool 押下列から押下/離しの edge を検出する（System のみ依存・純ロジック）。</summary>
    public sealed class PointerButtonState
    {
        private bool m_prev;

        public struct Edge
        {
            public bool Pressed;
            public bool JustPressed;
            public bool JustReleased;
        }

        public Edge Update(bool pressed)
        {
            var e = new Edge
            {
                Pressed = pressed,
                JustPressed = pressed && !m_prev,
                JustReleased = !pressed && m_prev,
            };
            m_prev = pressed;
            return e;
        }

        public void Reset() => m_prev = false;
    }
}
