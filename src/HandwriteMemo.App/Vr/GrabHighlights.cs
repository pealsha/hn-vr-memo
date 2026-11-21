using HandwriteMemo.Core;

namespace HandwriteMemo.App;

// Presentation only: GrabScene owns selection and acquisition distances.
// A new hover appears immediately; switching away normally needs 120 ms without
// the displayed candidate. Board/resize switches preview the new action immediately.
internal sealed class GrabHighlights
{
    internal const double ChangeDelaySeconds = .12;
    private readonly Grabbable?[] _shown = new Grabbable?[2];
    private readonly double?[] _changedAt = new double?[2];

    public bool Contains(Grabbable item) => _shown.Contains(item);

    public void Reset()
    {
        Array.Clear(_shown);
        Array.Clear(_changedAt);
    }

    public void Update(GrabFrame input, GrabFrameResult result, double seconds)
    {
        UpdateHand(HandSide.Left, input.Left, result.LeftCandidate);
        UpdateHand(HandSide.Right, input.Right, result.RightCandidate);

        void UpdateHand(HandSide hand, HandInput sample, Grabbable? candidate)
        {
            int i = (int)hand;
            // Do not retain hover when the user has picked something up, tracking fails, or the board closes.
            if (!input.IsVisible || input.Recenter || !sample.IsTracked || !sample.ButtonsValid ||
                ((result.Pen.Holder == hand || result.Eraser.Holder == hand) && candidate != Grabbable.ResizeHandle) ||
                result.BoardHolder == hand || result.ResizeHolder == hand)
            {
                _shown[i] = null;
                _changedAt[i] = null;
                return;
            }
            // Moving and resizing must preview the action that the next grip will take.
            // Keep the pen/eraser grace period, but never show the opposite corner action.
            bool cornerActionChanged = _shown[i] == Grabbable.Board && candidate == Grabbable.ResizeHandle ||
                _shown[i] == Grabbable.ResizeHandle && candidate == Grabbable.Board;
            if (_shown[i] is null || _shown[i] == candidate || cornerActionChanged)
            {
                _shown[i] = candidate;
                _changedAt[i] = null;
                return;
            }
            _changedAt[i] ??= seconds;
            if (seconds - _changedAt[i]!.Value >= ChangeDelaySeconds)
            {
                _shown[i] = candidate;
                _changedAt[i] = null;
            }
        }
    }
}
