package com.knossosnet.knossosnet.overlay;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.StateListDrawable;
import android.widget.Button;

/** Centralized appearance settings shared by every touch HUD control. */
public final class HudStyle {
    // Change this value, or call setBackgroundOpacity(), to tune the entire HUD.
    private static float backgroundOpacity = 0.10f;

    private static final int COLOR_PANEL = Color.rgb(5, 18, 28);
    private static final int COLOR_PANEL_ALT = Color.rgb(11, 48, 64);
    private static final int COLOR_ACCENT = Color.rgb(91, 225, 247);
    private static final int COLOR_PRESSED = Color.rgb(255, 183, 77);
    private static final int COLOR_PRESSED_STROKE = Color.rgb(255, 224, 163);
    private static final int COLOR_TEXT = Color.rgb(239, 252, 255);
    private static final int COLOR_PRESSED_TEXT = Color.rgb(16, 32, 42);

    private HudStyle() {
    }

    public static void setBackgroundOpacity(float opacity) {
        backgroundOpacity = Math.max(0f, Math.min(1f, opacity));
    }

    public static void ensureVisible() {
        if (backgroundOpacity <= 0.1f) {
            setBackgroundOpacity(0.20f);
        }
    }

    public static float getBackgroundOpacity() {
        return backgroundOpacity;
    }

    public static int withOpacity(int color, float multiplier) {
        int alpha = Math.round(255f * backgroundOpacity * multiplier);
        return Color.argb(Math.max(0, Math.min(255, alpha)),
                Color.red(color), Color.green(color), Color.blue(color));
    }

    public static void applyTo(Button button) {
        Context context = button.getContext();
        float density = context.getResources().getDisplayMetrics().density;
        button.setBackgroundTintList(null);

        GradientDrawable normal = new GradientDrawable(
                GradientDrawable.Orientation.TOP_BOTTOM,
                new int[] {
                        withOpacity(COLOR_PANEL_ALT, 1f),
                        withOpacity(COLOR_PANEL, 1f)
                });
        normal.setCornerRadius(4f * density);
        normal.setStroke(Math.max(1, Math.round(density)),
                withOpacity(COLOR_ACCENT, 1f));

        GradientDrawable pressed = new GradientDrawable(
                GradientDrawable.Orientation.TOP_BOTTOM,
                new int[] {
                        withOpacity(COLOR_PRESSED, 2.8f),
                        withOpacity(Color.rgb(183, 106, 34), 2.8f)
                });
        pressed.setCornerRadius(4f * density);
        pressed.setStroke(Math.max(1, Math.round(density)),
                withOpacity(COLOR_PRESSED_STROKE, 4.5f));

        StateListDrawable states = new StateListDrawable();
        states.addState(new int[] { android.R.attr.state_pressed }, pressed);
        states.addState(new int[0], normal);
        button.setBackground(states);

        button.setTextColor(new ColorStateList(
                new int[][] {
                        new int[] { android.R.attr.state_pressed },
                        new int[0]
                },
                new int[] {
                        withOpacity(COLOR_PRESSED_TEXT, 4.5f),
                        withOpacity(COLOR_TEXT, 1f)
                }));
    }
}
