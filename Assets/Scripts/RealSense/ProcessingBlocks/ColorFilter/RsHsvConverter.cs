using UnityEngine;

public static class RsHsvConverter
{
    public static void RgbToHsv(byte r, byte g, byte b, out Vector3 hsv)
    {
        float fr = r / 255f;
        float fg = g / 255f;
        float fb = b / 255f;

        float max = Mathf.Max(fr, Mathf.Max(fg, fb));
        float min = Mathf.Min(fr, Mathf.Min(fg, fb));
        float delta = max - min;

        float h = 0f;
        float s = (max == 0f) ? 0f : (delta / max);
        float v = max;

        if (delta != 0f)
        {
            if (max == fr)
            {
                h = (fg - fb) / delta;
                if (h < 0f) h += 6f;
            }
            else if (max == fg)
            {
                h = (fb - fr) / delta + 2f;
            }
            else // max == fb
            {
                h = (fr - fg) / delta + 4f;
            }
            h /= 6f;
        }

        hsv.x = h;
        hsv.y = s;
        hsv.z = v;
    }
}