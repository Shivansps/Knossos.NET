package com.knossosnet.knossosnet.overlay;

import android.annotation.SuppressLint;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.Typeface;
import android.util.AttributeSet;
import android.util.SparseBooleanArray;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.View;

/**
 * Analog mouse control with a separated, multi-touch outer action ring.
 *
 * A pointer that starts in the joystick keeps steering after leaving the base.
 * Crossing the outer threshold also holds the ring action, while another finger
 * can press the ring without taking control away from the joystick pointer.
 */
public class RadialDpadView extends View {

    public interface OnRingActionListener {
        void onRingAction(boolean pressed);
    }

    private static final int COLOR_PANEL = Color.rgb(5, 18, 28);
    private static final int COLOR_PANEL_STRONG = Color.rgb(7, 25, 36);
    private static final int COLOR_ACCENT = Color.rgb(91, 225, 247);
    private static final int COLOR_TEXT = Color.rgb(239, 252, 255);
    private static final int COLOR_PRESSED = Color.rgb(255, 183, 77);
    private static final int COLOR_PRESSED_TEXT = Color.rgb(16, 32, 42);

    private final Paint fillPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint linePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint glowPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint knobPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Path ringPath = new Path();
    private final SparseBooleanArray tabPointers = new SparseBooleanArray();
    private final SparseBooleanArray directRingPointers = new SparseBooleanArray();

    private boolean floating = false;
    private float deadzone = 0.12f;
    private boolean alwaysVisibleWhenFixed = true;
    private boolean visible = false;

    private float centerX;
    private float centerY;
    private float outerRadius;
    private float ringInnerRadius;
    private float joystickRadius;
    private float knobRadius;
    private float knobX;
    private float knobY;

    private int joystickPointerId = -1;
    private int tabPointerCount = 0;
    private float currentNx = 0f;
    private float currentNy = 0f;
    private OnRingActionListener ringActionListener;

    private final Runnable mouseTicker = new Runnable() {
        @Override
        public void run() {
            if (joystickPointerId != -1) {
                NativeBridge.mouseTick(currentNx, currentNy);
                postOnAnimation(this);
            }
        }
    };

    public RadialDpadView(Context context) {
        this(context, null);
    }

    public RadialDpadView(Context context, AttributeSet attrs) {
        this(context, attrs, 0);
    }

    public RadialDpadView(Context context, AttributeSet attrs, int defStyleAttr) {
        super(context, attrs, defStyleAttr);
        setFocusable(false);
        setClickable(true);
        setHapticFeedbackEnabled(true);

        fillPaint.setStyle(Paint.Style.FILL);

        linePaint.setStyle(Paint.Style.STROKE);
        linePaint.setStrokeWidth(dp(1.5f));
        linePaint.setColor(HudStyle.withOpacity(COLOR_ACCENT, 1f));

        glowPaint.setStyle(Paint.Style.STROKE);
        glowPaint.setStrokeWidth(dp(5f));
        glowPaint.setColor(HudStyle.withOpacity(COLOR_ACCENT, 0.6f));

        knobPaint.setStyle(Paint.Style.FILL);
        knobPaint.setColor(HudStyle.withOpacity(COLOR_ACCENT, 1.5f));

        textPaint.setColor(HudStyle.withOpacity(COLOR_TEXT, 1f));
        textPaint.setTextAlign(Paint.Align.CENTER);
        textPaint.setTypeface(Typeface.create("sans-serif-condensed", Typeface.BOLD));
        textPaint.setShadowLayer(dp(1.5f), 0f, dp(1f), Color.BLACK);
    }

    public void setFloating(boolean floating) {
        this.floating = floating;
        if (joystickPointerId == -1) {
            visible = floating ? false : alwaysVisibleWhenFixed;
            if (visible) resetKnob();
            invalidate();
        }
    }

    public boolean isFloating() {
        return floating;
    }

    public void setDeadzone(float deadzone) {
        this.deadzone = Math.max(0f, Math.min(0.5f, deadzone));
    }

    public float getDeadzone() {
        return deadzone;
    }

    public void setAlwaysVisibleWhenFixed(boolean visible) {
        alwaysVisibleWhenFixed = visible;
        if (!floating && joystickPointerId == -1) {
            this.visible = visible;
            resetKnob();
            invalidate();
        }
    }

    public void setOnRingActionListener(OnRingActionListener listener) {
        ringActionListener = listener;
    }

    private void resetKnob() {
        knobX = centerX;
        knobY = centerY;
    }

    @Override
    protected void onSizeChanged(int width, int height, int oldWidth, int oldHeight) {
        outerRadius = Math.max(0f, 0.5f * Math.min(width, height) - dp(4f));
        ringInnerRadius = outerRadius * 0.80f;
        joystickRadius = outerRadius * 0.55f;
        knobRadius = joystickRadius * 0.36f;
        centerX = width * 0.5f;
        centerY = height * 0.5f;
        resetKnob();
        if (!floating && alwaysVisibleWhenFixed && joystickPointerId == -1) visible = true;
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        if (!visible || outerRadius <= 0f) return;

        glowPaint.setColor(HudStyle.withOpacity(COLOR_ACCENT, 0.6f));
        linePaint.setColor(HudStyle.withOpacity(COLOR_ACCENT, 1f));
        knobPaint.setColor(HudStyle.withOpacity(COLOR_ACCENT, 1.5f));

        ringPath.reset();
        ringPath.setFillType(Path.FillType.EVEN_ODD);
        ringPath.addCircle(centerX, centerY, outerRadius, Path.Direction.CW);
        ringPath.addCircle(centerX, centerY, ringInnerRadius, Path.Direction.CW);
        fillPaint.setColor(tabPointerCount > 0
                ? HudStyle.withOpacity(COLOR_PRESSED, 2.8f)
                : HudStyle.withOpacity(COLOR_PANEL_STRONG, 1.2f));
        canvas.drawPath(ringPath, fillPaint);

        canvas.drawCircle(centerX, centerY, outerRadius, glowPaint);
        canvas.drawCircle(centerX, centerY, ringInnerRadius, glowPaint);
        canvas.drawCircle(centerX, centerY, outerRadius, linePaint);
        canvas.drawCircle(centerX, centerY, ringInnerRadius, linePaint);

        fillPaint.setColor(HudStyle.withOpacity(COLOR_PANEL, 1f));
        canvas.drawCircle(centerX, centerY, joystickRadius, fillPaint);
        canvas.drawCircle(centerX, centerY, joystickRadius, glowPaint);
        canvas.drawCircle(centerX, centerY, joystickRadius, linePaint);

        float cross = joystickRadius * 0.24f;
        canvas.drawLine(centerX - cross, centerY, centerX + cross, centerY, linePaint);
        canvas.drawLine(centerX, centerY - cross, centerX, centerY + cross, linePaint);
        canvas.drawCircle(knobX, knobY, knobRadius, knobPaint);
        canvas.drawCircle(knobX, knobY, knobRadius, linePaint);

        textPaint.setTextSize(Math.max(dp(10f), (outerRadius - ringInnerRadius) * 0.55f));
        textPaint.setColor(tabPointerCount > 0
                ? HudStyle.withOpacity(COLOR_PRESSED_TEXT, 4.5f)
                : HudStyle.withOpacity(COLOR_TEXT, 1f));
        Paint.FontMetrics metrics = textPaint.getFontMetrics();
        float textCenterY = centerY - (outerRadius + ringInnerRadius) * 0.5f;
        float baseline = textCenterY - (metrics.ascent + metrics.descent) * 0.5f;
        canvas.drawText("TAB", centerX, baseline, textPaint);
    }

    @SuppressLint("ClickableViewAccessibility")
    @Override
    public boolean onTouchEvent(MotionEvent event) {
        int action = event.getActionMasked();
        switch (action) {
            case MotionEvent.ACTION_DOWN:
            case MotionEvent.ACTION_POINTER_DOWN: {
                int index = event.getActionIndex();
                int pointerId = event.getPointerId(index);
                float x = event.getX(index);
                float y = event.getY(index);
                float distance = distanceFromCenter(x, y);

                if (joystickPointerId == -1 && (floating || distance <= joystickRadius)) {
                    if (floating) {
                        centerX = x;
                        centerY = y;
                        resetKnob();
                    }
                    joystickPointerId = pointerId;
                    performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
                    startControl(x, y);
                    return true;
                }

                if (isDirectRingHit(distance, false)) {
                    directRingPointers.put(pointerId, true);
                    setTabContribution(pointerId, true, true);
                    return true;
                }
                return joystickPointerId != -1 || directRingPointers.size() > 0;
            }

            case MotionEvent.ACTION_MOVE: {
                if (joystickPointerId != -1) {
                    int index = event.findPointerIndex(joystickPointerId);
                    if (index >= 0) updateFromTouch(event.getX(index), event.getY(index));
                }
                reconcileTabContributions(event);
                return joystickPointerId != -1 || directRingPointers.size() > 0;
            }

            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_POINTER_UP: {
                int index = event.getActionIndex();
                int pointerId = event.getPointerId(index);
                boolean handled = pointerId == joystickPointerId
                        || directRingPointers.get(pointerId, false);
                setTabContribution(pointerId, false, false);
                directRingPointers.delete(pointerId);
                if (pointerId == joystickPointerId) stopControl();
                if (action == MotionEvent.ACTION_UP && handled) performClick();
                return handled || joystickPointerId != -1 || directRingPointers.size() > 0;
            }

            case MotionEvent.ACTION_CANCEL:
                releaseAllControls();
                return true;

            default:
                return joystickPointerId != -1 || directRingPointers.size() > 0;
        }
    }

    private float distanceFromCenter(float x, float y) {
        return (float) Math.hypot(x - centerX, y - centerY);
    }

    private boolean isDirectRingHit(float distance, boolean retain) {
        float inner = outerRadius * (retain ? 0.69f : 0.75f);
        float outer = outerRadius * (retain ? 1.34f : 1.15f);
        return distance >= inner && distance <= outer;
    }

    private void reconcileTabContributions(MotionEvent event) {
        int previousCount = tabPointerCount;
        boolean newlyPressed = false;

        if (joystickPointerId != -1) {
            int eventIndex = event.findPointerIndex(joystickPointerId);
            if (eventIndex >= 0) {
                float distance = distanceFromCenter(
                        event.getX(eventIndex), event.getY(eventIndex));
                boolean previous = tabPointers.get(joystickPointerId, false);
                float threshold = outerRadius * (previous ? 0.72f : 0.78f);
                boolean next = distance >= threshold;
                updateTabMap(joystickPointerId, next);
                newlyPressed |= !previous && next;
            }
        }

        for (int capturedIndex = 0;
             capturedIndex < directRingPointers.size();
             capturedIndex++) {
            int pointerId = directRingPointers.keyAt(capturedIndex);
            int eventIndex = event.findPointerIndex(pointerId);
            if (eventIndex < 0) continue;
            boolean previous = tabPointers.get(pointerId, false);
            float distance = distanceFromCenter(
                    event.getX(eventIndex), event.getY(eventIndex));
            boolean next = isDirectRingHit(distance, previous);
            updateTabMap(pointerId, next);
            newlyPressed |= !previous && next;
        }

        tabPointerCount = tabPointers.size();
        if (previousCount == 0 && tabPointerCount > 0 && ringActionListener != null) {
            ringActionListener.onRingAction(true);
        } else if (previousCount > 0 && tabPointerCount == 0 && ringActionListener != null) {
            ringActionListener.onRingAction(false);
        }
        if (newlyPressed) performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
        invalidate();
    }

    private void updateTabMap(int pointerId, boolean pressed) {
        if (pressed) tabPointers.put(pointerId, true);
        else tabPointers.delete(pointerId);
    }

    private void startControl(float x, float y) {
        visible = true;
        knobX = centerX;
        knobY = centerY;
        currentNx = 0f;
        currentNy = 0f;
        invalidate();
        NativeBridge.mouseStart();
        updateFromTouch(x, y);
        removeCallbacks(mouseTicker);
        postOnAnimation(mouseTicker);
    }

    private void stopControl() {
        joystickPointerId = -1;
        currentNx = 0f;
        currentNy = 0f;
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
        float dx = x - centerX;
        float dy = y - centerY;
        float clampedLength = (float) Math.hypot(dx, dy);
        if (clampedLength > joystickRadius) {
            dx *= joystickRadius / clampedLength;
            dy *= joystickRadius / clampedLength;
            clampedLength = joystickRadius;
        }

        knobX = centerX + dx;
        knobY = centerY + dy;
        invalidate();

        float normalized = joystickRadius > 0f ? clampedLength / joystickRadius : 0f;
        if (normalized < deadzone) {
            currentNx = 0f;
            currentNy = 0f;
        } else {
            currentNx = dx / joystickRadius;
            currentNy = -dy / joystickRadius;
        }
    }

    private void setTabContribution(int pointerId, boolean pressed, boolean haptic) {
        boolean previous = tabPointers.get(pointerId, false);
        if (previous == pressed) return;

        if (pressed) {
            tabPointers.put(pointerId, true);
            tabPointerCount++;
            if (tabPointerCount == 1 && ringActionListener != null) {
                ringActionListener.onRingAction(true);
            }
            if (haptic) performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
        } else {
            tabPointers.delete(pointerId);
            tabPointerCount = Math.max(0, tabPointerCount - 1);
            if (tabPointerCount == 0 && ringActionListener != null) {
                ringActionListener.onRingAction(false);
            }
        }
        invalidate();
    }

    public void releaseAllControls() {
        if (tabPointerCount > 0 && ringActionListener != null) {
            ringActionListener.onRingAction(false);
        }
        tabPointers.clear();
        directRingPointers.clear();
        tabPointerCount = 0;
        if (joystickPointerId != -1) stopControl();
        invalidate();
    }

    @Override
    public boolean performClick() {
        super.performClick();
        return true;
    }

    @Override
    protected void onVisibilityChanged(View changedView, int visibility) {
        super.onVisibilityChanged(changedView, visibility);
        if (visibility != VISIBLE) releaseAllControls();
    }

    @Override
    protected void onDetachedFromWindow() {
        releaseAllControls();
        super.onDetachedFromWindow();
    }

    private float dp(float value) {
        return value * getResources().getDisplayMetrics().density;
    }
}
