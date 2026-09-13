using UnityEngine;

namespace Hapbeat.Boxing
{
    // Fixed, readable combinations. Aim is committed before the strike, so leaning works.
    public sealed class BoxingOpponent
    {
        public Vector3 Head { get; private set; }
        public Vector3 Body { get; private set; }
        public Vector3 Left { get; private set; }
        public Vector3 Right { get; private set; }
        public Vector3 Root { get; private set; }
        public bool Striking { get; private set; }
        public bool Telegraphing { get; private set; }
        public int AttackId { get; private set; }
        public bool AttackLeft { get; private set; }
        public string Cue { get; private set; } = "READY";
        public int CompletedAttacks { get; private set; }
        private float clock, attackTime, wait, reaction;
        private int pattern;
        private bool attacking;
        private Vector3 aim, strikeStart;
        private float height = 1.65f;
        private readonly BoxingTuning tuning;
        public BoxingOpponent(BoxingTuning tuning) { this.tuning = tuning; Reset(1.65f); }
        public void Reset(float playerHeight)
        {
            height = Mathf.Clamp(playerHeight, 1.25f, 1.95f); clock = attackTime = reaction = 0;
            pattern = AttackId = CompletedAttacks = 0; wait = 0.7f; attacking = Striking = Telegraphing = false;
            SetRestPose();
        }
        public void React(float gain) => reaction = Mathf.Max(reaction, 0.10f + gain * 0.15f);
        public void Tick(float dt, Vector3 playerHead, bool fighting)
        {
            if (dt <= 0) return;
            clock += dt; reaction = Mathf.Max(0, reaction - dt);
            SetRestPose(); Striking = Telegraphing = false;
            if (!fighting) { Cue = "READY"; return; }
            if (!attacking)
            {
                wait -= dt;
                if (wait > 0) { Cue = "MOVE / BREATHE"; return; }
                attacking = true; attackTime = 0; AttackId++; AttackLeft = pattern % 3 != 1;
                aim = playerHead; strikeStart = AttackLeft ? Left : Right;
            }
            attackTime += dt;
            float pre = tuning.telegraphSeconds, strike = tuning.strikeSeconds, recover = tuning.recoverSeconds;
            Vector3 rest = AttackLeft ? Left : Right;
            Vector3 fist;
            bool hook = pattern % 6 >= 4;
            if (attackTime < pre)
            {
                Telegraphing = true; Cue = hook ? "HOOK" : AttackLeft ? "JAB" : "CROSS";
                fist = rest + new Vector3(AttackLeft ? -0.05f : 0.05f, 0.03f, 0.12f) * Mathf.Sin(attackTime / pre * Mathf.PI * 0.5f);
                strikeStart = fist;
            }
            else if (attackTime < pre + strike)
            {
                Striking = true; float t = (attackTime - pre) / strike;
                fist = Vector3.Lerp(strikeStart, aim + Vector3.back * 0.12f, t);
                if (hook) fist += Vector3.right * (AttackLeft ? -0.35f : 0.35f) * Mathf.Sin(t * Mathf.PI);
            }
            else
            {
                float t = Mathf.Clamp01((attackTime - pre - strike) / recover);
                fist = Vector3.Lerp(aim + Vector3.back * 0.12f, rest, t * t * (3 - 2 * t));
                Cue = "YOUR TURN";
                if (t >= 1) { attacking = false; pattern++; CompletedAttacks++; wait = pattern % 3 == 1 ? 0.18f : tuning.enemyInterval; }
            }
            if (AttackLeft) Left = fist; else Right = fist;
        }
        private void SetRestPose()
        {
            Root = new Vector3(Mathf.Sin(clock * 0.72f) * 0.16f, 0, 1.08f + Mathf.Sin(clock * 0.91f) * 0.10f);
            float bob = Mathf.Sin(clock * 3) * 0.022f;
            Head = Root + new Vector3(Mathf.Sin(clock * 0.9f) * 0.035f, height + bob, reaction * 0.24f);
            Body = Root + new Vector3(0, height - 0.43f + bob, 0.015f);
            Left = Head + new Vector3(-0.26f, -0.18f, -0.22f);
            Right = Head + new Vector3(0.26f, -0.18f, -0.22f);
        }
    }
}
