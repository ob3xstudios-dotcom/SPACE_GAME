using System;

namespace Game.Core
{
    [Serializable]
    public class IntResource
    {
        public int Current { get; private set; }
        public int Max { get; private set; }

        public event Action<int, int> OnChanged; // (current, max)
        public event Action OnDepleted;

        public IntResource(int max, int current = -1)
        {
            Max = Math.Max(0, max);
            Current = (current < 0) ? Max : Clamp(current, 0, Max);
        }

        public void SetMax(int newMax, bool keepRatio = false)
        {
            newMax = Math.Max(0, newMax);

            if (keepRatio && Max > 0)
            {
                float ratio = (float)Current / Max;
                Max = newMax;
                Current = Clamp((int)Math.Round(ratio * Max), 0, Max);
            }
            else
            {
                Max = newMax;
                Current = Clamp(Current, 0, Max);
            }

            RaiseChanged();
        }

        public void SetCurrent(int value)
        {
            int prev = Current;
            Current = Clamp(value, 0, Max);

            if (Current != prev)
            {
                RaiseChanged();
                if (Current <= 0) OnDepleted?.Invoke();
            }
        }

        public bool Spend(int amount)
        {
            if (amount <= 0) return true;
            if (Current < amount) return false;

            Current -= amount;
            RaiseChanged();
            if (Current <= 0) OnDepleted?.Invoke();
            return true;
        }

        public void Restore(int amount)
        {
            if (amount <= 0) return;
            int prev = Current;
            Current = Clamp(Current + amount, 0, Max);
            if (Current != prev) RaiseChanged();
        }

        public void Fill()
        {
            Current = Max;
            RaiseChanged();
        }

        private void RaiseChanged() => OnChanged?.Invoke(Current, Max);

        private static int Clamp(int v, int min, int max) => (v < min) ? min : (v > max) ? max : v;
    }
}
