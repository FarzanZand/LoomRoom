using System;
using System.Collections.Generic;

// Weighted random picks: breakables, wall lights, room shapes and styles, enemies, room profiles,
// boons and surface variants. An entry whose weight is not above zero is never picked. The order of
// work is fixed (add up the weights, one roll, walk the list), so a seed always lands on the same entry.
public static class WeightedPick
{
    // drawWhenEmpty: roll even when nothing can be picked, for seeded streams that count on the draw.
    // singleTotal: add the weights up in single precision, as the room profile and enemy picks always have.
    public static T Choose<T>(IList<T> items, Func<T, float> weight, System.Random random, bool drawWhenEmpty = false, bool singleTotal = false) where T : class
    {
        if (items == null) return null;
        double total = singleTotal ? TotalSingle(items, weight) : Total(items, weight);
        if (total <= 0 && !drawWhenEmpty) return null;
        int index = Land(items, weight, random.NextDouble() * total);
        return index >= 0 ? items[index] : null;
    }

    // Single precision throughout, from a roll in [0, 1) (UnityEngine.Random.value or a per-tile hash).
    public static T Choose<T>(IList<T> items, Func<T, float> weight, float roll01) where T : class
    {
        if (items == null) return null;
        float total = TotalSingle(items, weight);
        if (total <= 0) return null;
        int index = LandSingle(items, weight, roll01 * total);
        return index >= 0 ? items[index] : null;
    }

    public static double Total<T>(IList<T> items, Func<T, float> weight)
    {
        double total = 0;
        for (int i = 0; i < items.Count; i++) { float w = weight(items[i]); if (w > 0) total += w; }
        return total;
    }

    // start: a weight ahead of the list that belongs to none of its entries (a base material).
    public static float TotalSingle<T>(IList<T> items, Func<T, float> weight, float start = 0)
    {
        float total = start;
        for (int i = 0; i < items.Count; i++) { float w = weight(items[i]); if (w > 0) total += w; }
        return total;
    }

    // The entry a roll in [0, total) lands on, or -1.
    public static int Land<T>(IList<T> items, Func<T, float> weight, double roll)
    {
        for (int i = 0; i < items.Count; i++)
        {
            float w = weight(items[i]);
            if (!(w > 0)) continue;
            roll -= w;
            if (roll < 0) return i;
        }
        return -1;
    }

    public static int LandSingle<T>(IList<T> items, Func<T, float> weight, float roll)
    {
        for (int i = 0; i < items.Count; i++)
        {
            float w = weight(items[i]);
            if (!(w > 0)) continue;
            roll -= w;
            if (roll < 0) return i;
        }
        return -1;
    }
}
