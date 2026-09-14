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
        public bool Guarding { get; private set; }
        public float GuardWeight { get; private set; }
        public int AttackId { get; private set; }
        public bool AttackLeft { get; private set; }
        public int CompletedAttacks { get; private set; }
        private float clock, attackTime, wait, reaction;
        private int pattern;
        private bool attacking, blocked;
        private Vector3 aim, strikeStart, blockedAt;
        private float height = 1.65f;
        private float guardWait, guardTime;
        private System.Random random;
        private readonly BoxingTuning tuning;
        public BoxingOpponent(BoxingTuning tuning) { this.tuning = tuning; Reset(1.65f); }
        public void Reset(float playerHeight)
        {
            height = Mathf.Clamp(playerHeight, 1.25f, 1.95f); clock = attackTime = reaction = 0;
            pattern = AttackId = CompletedAttacks = 0; wait = 0.7f; attacking = Striking = Telegraphing = blocked = false;
            random = new System.Random(1701); guardWait = tuning.guardInterval; guardTime = 0; Guarding = false; GuardWeight = 0;
            SetRestPose();
        }
        public void React(float gain) => reaction = Mathf.Max(reaction, 0.10f + gain * 0.15f);
        public void Block(Vector3 contact)
        {
            if (!Striking) return;
            blocked = true; blockedAt = contact;
            attackTime = tuning.telegraphSeconds + tuning.strikeSeconds;
            Striking = Telegraphing = false;
            if (AttackLeft) Left = contact; else Right = contact;
        }
        public void Tick(float dt, Vector3 playerHead, bool fighting)
        {
            if (dt <= 0) return;
            clock += dt; reaction = Mathf.Max(0, reaction - dt);
            SetRestPose(); Striking = Telegraphing = Guarding = false; GuardWeight = 0;
            if (!fighting) return;
            if (!attacking)
            {
                guardWait -= dt;
                if (guardTime > 0 || guardWait <= 0)
                {
                    guardTime += dt; Guarding = true;
                    GuardWeight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(Mathf.Min(guardTime, tuning.guardSeconds - guardTime) / 0.18f));
                    Left = Vector3.Lerp(Left, Head + new Vector3(-0.105f, -0.025f, -0.25f), GuardWeight);
                    Right = Vector3.Lerp(Right, Head + new Vector3(0.105f, -0.025f, -0.25f), GuardWeight);
                    if (guardTime >= tuning.guardSeconds) { guardTime = 0; guardWait = tuning.guardInterval * (0.65f + (float)random.NextDouble()); Guarding = false; }
                    return;
                }
                wait -= dt;
                if (wait > 0) return;
                attacking = true; blocked = false; attackTime = 0; AttackId++; AttackLeft = pattern % 3 != 1;
                aim = playerHead; strikeStart = AttackLeft ? Left : Right;
            }
            attackTime += dt;
            float pre = tuning.telegraphSeconds, strike = tuning.strikeSeconds, recover = tuning.recoverSeconds;
            Vector3 rest = AttackLeft ? Left : Right;
            Vector3 fist;
            bool hook = pattern % 6 >= 4;
            if (attackTime < pre)
            {
                Telegraphing = true;
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
                fist = Vector3.Lerp(blocked ? blockedAt : aim + Vector3.back * 0.12f, rest, t * t * (3 - 2 * t));
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
