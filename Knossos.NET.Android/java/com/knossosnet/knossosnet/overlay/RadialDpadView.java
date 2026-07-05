package com.knossosnet.knossosnet.overlay;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.PorterDuff;
import android.util.AttributeSet;
import android.view.MotionEvent;
import android.view.View;

public class RadialDpadView extends View {

    private final Paint pBase = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pKnob = new Paint(Paint.ANTI_ALIAS_FLAG);

    private boolean floating = false;
    private float deadzone = 0.12f;
    private boolean alwaysVisibleWhenFixed = true;

    private float cx, cy;
    private float rBase;
    private float rKnob;
    private float knobX, knobY;

    private boolean visible = false;
    private int activePid = -1;

    private float currentNx = 0f, currentNy = 0f;

    private final Runnable mouseTicker = new Runnable() {
        @Override public void run() {
            if (activePid != -1) {
                NativeBridge.mouseTick(currentNx, currentNy );
                postOnAnimation(this);
            }
        }
    };

    public RadialDpadView(Context c) { this(c, null); }
    public RadialDpadView(Context c, AttributeSet a) {
        super(c, a);
        setFocusable(false);
        setClickable(true);
        setHapticFeedbackEnabled(true);

        pBase.setStyle(Paint.Style.FILL);
        pBase.setColor(Color.WHITE);
        pBase.setAlpha(26);

        pKnob.setStyle(Paint.Style.FILL);
        pKnob.setColor(Color.WHITE);
        pKnob.setAlpha(77);
    }

    public void setFloating(boolean f) {
        this.floating = f;
        if (activePid == -1) {
            if (f) {
                visible = false;
            } else {
                visible = alwaysVisibleWhenFixed;
                if (visible) resetKnob();
            }
            invalidate();
        }
    }

    public boolean isFloating() { return floating; }
    public void setDeadzone(float dz) { this.deadzone = Math.max(0f, Math.min(0.5f, dz)); }
    public float getDeadzone() { return deadzone; }

    public void setAlwaysVisibleWhenFixed(boolean v) {
        this.alwaysVisibleWhenFixed = v;
        if (!floating && activePid == -1) {
            visible = v;
            resetKnob();
            invalidate();
        }
    }

    private void resetKnob() { knobX = cx; knobY = cy; }

    @Override protected void onSizeChanged(int w, int h, int oldw, int oldh) {
        rBase = 0.5f * Math.min(w, h) * 0.95f;
        rKnob = rBase * 0.40f;
        cx = w * 0.5f; cy = h * 0.5f;
        resetKnob();
        if (!floating && alwaysVisibleWhenFixed && activePid == -1) {
            visible = true;
        }
    }

    @Override protected void onDraw(Canvas c) {
        c.drawColor(0, PorterDuff.Mode.CLEAR);
        if (!visible) return;
        c.drawCircle(cx, cy, rBase, pBase);
        c.drawCircle(knobX, knobY, rKnob, pKnob);
    }

    @Override public boolean onTouchEvent(MotionEvent ev) {
        final int act = ev.getActionMasked();
        switch (act) {
            case MotionEvent.ACTION_DOWN:
            case MotionEvent.ACTION_POINTER_DOWN: {
                final int idx = ev.getActionIndex();
                final int pid = ev.getPointerId(idx);
                final float x = ev.getX(idx), y = ev.getY(idx);

                if (activePid == -1) {
                    if (floating) {
                        cx = x; cy = y;
                    } else {
                        if (!isInsideCircle(x, y)) return false;
                    }
                    activePid = pid;
                    performHapticFeedback(1);
                    startControl(x, y);
                    return true;
                }
                break;
            }
            case MotionEvent.ACTION_MOVE: {
                if (activePid != -1) {
                    final int idx = ev.findPointerIndex(activePid);
                    if (idx >= 0) updateFromTouch(ev.getX(idx), ev.getY(idx));
                }
                break;
            }
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_POINTER_UP:
            case MotionEvent.ACTION_CANCEL: {
                final int idx = ev.getActionIndex();
                final int pid = ev.getPointerId(idx);
                if (pid == activePid) {
                    stopControl();
                    return true;
                }
                break;
            }
        }
        return activePid != -1;
    }

    private boolean isInsideCircle(float x, float y) {
        float dx = x - cx, dy = y - cy;
        return (dx*dx + dy*dy) <= (rBase * rBase);
    }

    private void startControl(float x, float y) {
        visible = true;
        knobX = cx; knobY = cy;
        currentNx = currentNy = 0f;
        invalidate();
        NativeBridge.mouseStart();
        updateFromTouch(x, y);
        removeCallbacks(mouseTicker);
        postOnAnimation(mouseTicker);
    }

    private void stopControl() {
        activePid = -1;
        currentNx = currentNy = 0f;
        if (!floating && alwaysVisibleWhenFixed) {
            visible = true;
            resetKnob();
        } else {
            visible = false;
        }
        invalidate();
        removeCallbacks(mouseTicker);
        NativeBridge.mouseTick(0f, 0f);
        NativeBridge.mouseStop();
    }

    private void updateFromTouch(float x, float y) {
        float dx = x - cx, dy = y - cy;
        float len = (float) Math.hypot(dx, dy);
        if (len > rBase) { dx *= (rBase/len); dy *= (rBase/len); len = rBase; }
        knobX = cx + dx; knobY = cy + dy; invalidate();

        float norm = (rBase > 0f) ? (len / rBase) : 0f;
        if (norm < deadzone) {
            currentNx = 0f; currentNy = 0f;
        } else {
            currentNx = dx / rBase;
            currentNy = -dy / rBase;
        }
    }
}
