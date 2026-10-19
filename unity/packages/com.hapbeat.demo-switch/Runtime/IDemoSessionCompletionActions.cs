using System;
using System.Collections.Generic;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Optional, next to <see cref="IDemoSessionHost"/> on the registered scene host: the demo's own buttons on the
    /// completion panel, between もう一度 and 次へ / デモを終了 (for example another mode of the same demo). Like もう一度
    /// they appear only on a step with `retry`; a host without this interface, or with none to offer, keeps the panel as
    /// it was. Pressing one closes the panel (gameplay resumed, <see cref="DemoSession.Closed"/>) and then runs it, in the
    /// same step (no process restart).
    /// </summary>
    public interface IDemoSessionCompletionActions
    {
        /// <summary>The buttons for the completion panel being shown now (asked each time it is shown).</summary>
        IReadOnlyList<DemoSessionCompletionAction> CompletionActions { get; }
    }

    /// <summary>One of the demo's own completion panel buttons: its label and what it starts.</summary>
    public readonly struct DemoSessionCompletionAction
    {
        public readonly string Label;
        public readonly Action Run;

        public DemoSessionCompletionAction(string label, Action run)
        {
            Label = label;
            Run = run;
        }
    }
}
