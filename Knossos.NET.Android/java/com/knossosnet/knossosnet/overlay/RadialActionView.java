package com.knossosnet.knossosnet.overlay;

import android.annotation.SuppressLint;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.util.AttributeSet;
import android.util.SparseArray;
import android.util.SparseBooleanArray;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.View;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.IdentityHashMap;
import java.util.List;
import java.util.Map;

/**
 * A game-oriented radial group with one to three actions in its center and any
 * number of actions around the outside. Actions are configured by the host so
 * this view can be reused for weapons, targeting, communications, and similar
 * control clusters.
 */
public class RadialActionView extends View {

    public static final class Action {
        private final String label;
        private final String contentDescription;
        private final int[] keyCodes;

        public Action(String label, int... keyCodes) {
            this(label, label == null ? "" : label.replace('\n', ' '), keyCodes);
        }

        public Action(String label, String contentDescription, int... keyCodes) {
            if (label == null) throw new IllegalArgumentException("label cannot be null");
            if (keyCodes == null || keyCodes.length == 0) {
                throw new IllegalArgumentException("an action needs at least one key code");
            }
            this.label = label;
            this.contentDescription = contentDescription;
            this.keyCodes = Arrays.copyOf(keyCodes, keyCodes.length);
        }

        public String getLabel() {
            return label;
        }

        public String getContentDescription() {
            return contentDescription;
        }

        public int[] getKeyCodes() {
            return Arrays.copyOf(keyCodes, keyCodes.length);
        }
    }

    public interface OnActionListener {
        /**
         * Reports all action edges from one MotionEvent as a single change.
         * Shared key payloads can therefore stay held across pointer swaps or
         * while sliding between neighboring actions.
         */
        void onActionsChanged(List<Action> releasedActions, List<Action> pressedActions);
    }

    private static final int COLOR_PANEL = Color.rgb(5, 18, 28);
    private static final int COLOR_PANEL_ALT = Color.rgb(11, 35, 48);
    private static final int COLOR_ACCENT = Color.rgb(91, 225, 247);
    private static final int COLOR_TEXT = Color.rgb(239, 252, 255);
    private static final int COLOR_PRESSED = Color.rgb(255, 183, 77);
    private static final int COLOR_PRESSED_TEXT = Color.rgb(16, 32, 42);

    private final Paint fillPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint linePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint glowPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF outerBounds = new RectF();
    private final RectF innerBounds = new RectF();
    private final Path sectorPath = new Path();
    private final Path centerClipPath = new Path();

    private final List<Action> centerActions = new ArrayList<>();
    private final List<Action> outerActions = new ArrayList<>();
    private final SparseArray<Action> pointerActions = new SparseArray<>();
    private final SparseBooleanArray capturedPointers = new SparseBooleanArray();
    private final Map<Action, Integer> pressCounts = new IdentityHashMap<>();

    private OnActionListener actionListener;
    private float[] centerWeights = new float[0];
    private float innerRadiusRatio = 0.56f;
    private float firstOuterActionAngle = -90f;
    private float labelScale = 1f;
    private float centerX;
    private float centerY;
    private float outerRadius;
    private float innerRadius;

    public RadialActionView(Context context) {
        this(context, null);
    }

    public RadialActionView(Context context, AttributeSet attrs) {
        this(context, attrs, 0);
    }

    public RadialActionView(Context context, AttributeSet attrs, int defStyleAttr) {
        super(context, attrs, defStyleAttr);
        setClickable(true);
        setFocusable(false);
        setHapticFeedbackEnabled(true);

        fillPaint.setStyle(Paint.Style.FILL);

        linePaint.setStyle(Paint.Style.STROKE);
        linePaint.setStrokeWidth(dp(1.35f));
        linePaint.setColor(HudStyle.withOpacity(COLOR_ACCENT, 1f));

        glowPaint.setStyle(Paint.Style.STROKE);
        glowPaint.setStrokeWidth(dp(5f));
        glowPaint.setColor(HudStyle.withOpacity(COLOR_ACCENT, 0.6f));

        textPaint.setColor(HudStyle.withOpacity(COLOR_TEXT, 1f));
        textPaint.setTextAlign(Paint.Align.CENTER);
        textPaint.setTypeface(Typeface.create("sans-serif-condensed", Typeface.BOLD));
        textPaint.setShadowLayer(dp(1.5f), 0f, dp(1f), Color.BLACK);
    }

    public void setActions(List<Action> center, List<Action> outer) {
        if (center == null || center.isEmpty() || center.size() > 3) {
            throw new IllegalArgumentException("center must contain between one and three actions");
        }
        if (outer == null || outer.isEmpty()) {
            throw new IllegalArgumentException("outer must contain at least one action");
        }
        releaseAllActions();
        centerActions.clear();
        centerActions.addAll(center);
        resetCenterWeights();
        outerActions.clear();
        outerActions.addAll(outer);
        updateContentDescription();
        invalidate();
    }

    public void setCenterActions(Action... actions) {
        if (actions == null || actions.length == 0 || actions.length > 3) {
            throw new IllegalArgumentException("center must contain between one and three actions");
        }
        releaseAllActions();
        centerActions.clear();
        centerActions.addAll(Arrays.asList(actions));
        resetCenterWeights();
        updateContentDescription();
        invalidate();
    }

    public void setOuterActions(Action... actions) {
        if (actions == null || actions.length == 0) {
            throw new IllegalArgumentException("outer must contain at least one action");
        }
        releaseAllActions();
        outerActions.clear();
        outerActions.addAll(Arrays.asList(actions));
        updateContentDescription();
        invalidate();
    }

    public void setOnActionListener(OnActionListener listener) {
        this.actionListener = listener;
    }

    public void setInnerRadiusRatio(float ratio) {
        innerRadiusRatio = Math.max(0.38f, Math.min(0.72f, ratio));
        updateGeometry(getWidth(), getHeight());
        invalidate();
    }

    public void setFirstOuterActionAngle(float degrees) {
        firstOuterActionAngle = degrees;
        invalidate();
    }

    public void setLabelScale(float scale) {
        labelScale = Math.max(0.7f, Math.min(1.4f, scale));
        invalidate();
    }

    public void setCenterWeights(float... weights) {
        if (weights == null || weights.length != centerActions.size()) {
            throw new IllegalArgumentException("weights must match the center action count");
        }
        float total = 0f;
        for (float weight : weights) {
            if (weight <= 0f) throw new IllegalArgumentException("weights must be positive");
            total += weight;
        }
        if (total <= 0f) throw new IllegalArgumentException("weights must have a positive sum");
        centerWeights = Arrays.copyOf(weights, weights.length);
        invalidate();
    }

    private void resetCenterWeights() {
        centerWeights = new float[centerActions.size()];
        Arrays.fill(centerWeights, 1f);
    }

    @Override
    protected void onSizeChanged(int width, int height, int oldWidth, int oldHeight) {
        updateGeometry(width, height);
    }

    private void updateGeometry(int width, int height) {
        centerX = width * 0.5f;
        centerY = height * 0.5f;
        outerRadius = Math.max(0f, Math.min(width, height) * 0.5f - dp(4f));
        innerRadius = outerRadius * innerRadiusRatio;
        outerBounds.set(centerX - outerRadius, centerY - outerRadius,
                centerX + outerRadius, centerY + outerRadius);
        innerBounds.set(centerX - innerRadius, centerY - innerRadius,
                centerX + innerRadius, centerY + innerRadius);
        centerClipPath.reset();
        centerClipPath.addCircle(centerX, centerY, innerRadius, Path.Direction.CW);
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        if (centerActions.isEmpty() || outerActions.isEmpty() || outerRadius <= 0f) return;

        glowPaint.setColor(HudStyle.withOpacity(COLOR_ACCENT, 0.6f));
        linePaint.setColor(HudStyle.withOpacity(COLOR_ACCENT, 1f));

        drawOuterActions(canvas);
        drawCenterActions(canvas);

        canvas.drawCircle(centerX, centerY, outerRadius, glowPaint);
        canvas.drawCircle(centerX, centerY, innerRadius, glowPaint);
        canvas.drawCircle(centerX, centerY, outerRadius, linePaint);
        canvas.drawCircle(centerX, centerY, innerRadius, linePaint);
    }

    private void drawOuterActions(Canvas canvas) {
        final float sweep = 360f / outerActions.size();
        final float sectorStart = firstOuterActionAngle - sweep * 0.5f;

        for (int index = 0; index < outerActions.size(); index++) {
            Action action = outerActions.get(index);
            float start = sectorStart + index * sweep;

            sectorPath.reset();
            sectorPath.arcTo(outerBounds, start, sweep);
            sectorPath.arcTo(innerBounds, start + sweep, -sweep);
            sectorPath.close();
            fillPaint.setColor(isPressed(action)
                    ? HudStyle.withOpacity(COLOR_PRESSED, 2.8f)
                    : HudStyle.withOpacity(index % 2 == 0 ? COLOR_PANEL : COLOR_PANEL_ALT,
                            index % 2 == 0 ? 1f : 0.78f));
            canvas.drawPath(sectorPath, fillPaint);

            float boundaryRadians = (float) Math.toRadians(start);
            canvas.drawLine(
                    centerX + (float) Math.cos(boundaryRadians) * innerRadius,
                    centerY + (float) Math.sin(boundaryRadians) * innerRadius,
                    centerX + (float) Math.cos(boundaryRadians) * outerRadius,
                    centerY + (float) Math.sin(boundaryRadians) * outerRadius,
                    linePaint);

            float labelRadians = (float) Math.toRadians(firstOuterActionAngle + index * sweep);
            float labelRadius = innerRadius + (outerRadius - innerRadius) * 0.53f;
            float labelX = centerX + (float) Math.cos(labelRadians) * labelRadius;
            float labelY = centerY + (float) Math.sin(labelRadians) * labelRadius;
            float arcWidth = (float) (labelRadius * Math.sin(Math.min(Math.PI / 2, Math.toRadians(sweep * 0.42f))) * 2f);
            float textSize = Math.min((outerRadius - innerRadius) * 0.48f,
                    Math.max(dp(8f), arcWidth * 0.36f));
            textPaint.setColor(isPressed(action)
                    ? HudStyle.withOpacity(COLOR_PRESSED_TEXT, 4.5f)
                    : HudStyle.withOpacity(COLOR_TEXT, 1f));
            drawCenteredLabel(canvas, action.label, labelX, labelY, textSize * labelScale);
        }
    }

    private void drawCenterActions(Canvas canvas) {
        int save = canvas.save();
        canvas.clipPath(centerClipPath);
        for (int index = 0; index < centerActions.size(); index++) {
            Action action = centerActions.get(index);
            float top = centerBandTop(index);
            float bandHeight = centerBandHeight(index);
            fillPaint.setColor(isPressed(action)
                    ? HudStyle.withOpacity(COLOR_PRESSED, 2.8f)
                    : HudStyle.withOpacity(index % 2 == 0 ? COLOR_PANEL_ALT : COLOR_PANEL,
                            index % 2 == 0 ? 0.78f : 1f));
            canvas.drawRect(centerX - innerRadius, top,
                    centerX + innerRadius, top + bandHeight, fillPaint);

            float textSize = Math.min(bandHeight * 0.36f, innerRadius * 0.27f);
            textPaint.setColor(isPressed(action)
                    ? HudStyle.withOpacity(COLOR_PRESSED_TEXT, 4.5f)
                    : HudStyle.withOpacity(COLOR_TEXT, 1f));
            float labelY = top + bandHeight * 0.5f;
            float requestedSize = Math.max(dp(8f), textSize) * labelScale;
            drawCenteredLabel(canvas, action.label, centerX, labelY,
                    fitCenterTextSize(action.label, labelY, requestedSize));
        }
        canvas.restoreToCount(save);

        for (int index = 1; index < centerActions.size(); index++) {
            float y = centerBandTop(index);
            float dy = y - centerY;
            float halfChord = (float) Math.sqrt(Math.max(0f, innerRadius * innerRadius - dy * dy));
            canvas.drawLine(centerX - halfChord, y, centerX + halfChord, y, linePaint);
        }
    }

    private void drawCenteredLabel(Canvas canvas, String label, float x, float y, float textSize) {
        textPaint.setTextSize(textSize);
        String[] lines = label.split("\\n", -1);
        Paint.FontMetrics metrics = textPaint.getFontMetrics();
        float lineHeight = (metrics.descent - metrics.ascent) * 0.86f;
        float baseline = y - ((lines.length - 1) * lineHeight) * 0.5f
                - (metrics.ascent + metrics.descent) * 0.5f;
        for (int index = 0; index < lines.length; index++) {
            canvas.drawText(lines[index], x, baseline + index * lineHeight, textPaint);
        }
    }

    private float fitCenterTextSize(String label, float labelY, float requestedSize) {
        textPaint.setTextSize(requestedSize);
        String[] lines = label.split("\\n", -1);
        Paint.FontMetrics metrics = textPaint.getFontMetrics();
        float lineHeight = (metrics.descent - metrics.ascent) * 0.86f;
        float scale = 1f;

        for (int index = 0; index < lines.length; index++) {
            float lineCenterY = labelY
                    + (index - (lines.length - 1) * 0.5f) * lineHeight;
            float dy = lineCenterY - centerY;
            float halfChord = (float) Math.sqrt(
                    Math.max(0f, innerRadius * innerRadius - dy * dy));
            float availableWidth = Math.max(0f, halfChord * 2f - dp(10f));
            float measuredWidth = textPaint.measureText(lines[index]);
            if (measuredWidth > 0f) {
                scale = Math.min(scale, availableWidth / measuredWidth);
            }
        }

        return requestedSize * Math.min(1f, Math.max(0.55f, scale));
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
                Action hit = hitTest(event.getX(index), event.getY(index));
                if (hit == null && capturedPointers.size() == 0) return false;
                if (hit == null) return true;
                capturedPointers.put(pointerId, true);
                updatePointerAction(pointerId, hit, true);
                return true;
            }
            case MotionEvent.ACTION_MOVE:
                updateCapturedPointers(event);
                return capturedPointers.size() > 0;
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_POINTER_UP: {
                int index = event.getActionIndex();
                int pointerId = event.getPointerId(index);
                boolean handled = capturedPointers.get(pointerId, false);
                updatePointerAction(pointerId, null, false);
                capturedPointers.delete(pointerId);
                if (action == MotionEvent.ACTION_UP && handled) performClick();
                return handled || capturedPointers.size() > 0;
            }
            case MotionEvent.ACTION_CANCEL:
                releaseAllActions();
                return true;
            default:
                return capturedPointers.size() > 0;
        }
    }

    private Action hitTest(float x, float y) {
        if (centerActions.isEmpty() || outerActions.isEmpty()) return null;
        float dx = x - centerX;
        float dy = y - centerY;
        float distance = (float) Math.hypot(dx, dy);
        if (distance > outerRadius) return null;

        if (distance <= innerRadius) {
            float target = ((y - (centerY - innerRadius)) / (innerRadius * 2f))
                    * totalCenterWeight();
            float accumulated = 0f;
            for (int index = 0; index < centerActions.size(); index++) {
                accumulated += centerWeights[index];
                if (target <= accumulated || index == centerActions.size() - 1) {
                    return centerActions.get(index);
                }
            }
        }

        float degrees = (float) Math.toDegrees(Math.atan2(dy, dx));
        float sweep = 360f / outerActions.size();
        float relative = normalizeDegrees(degrees - firstOuterActionAngle + sweep * 0.5f);
        int index = Math.min(outerActions.size() - 1, (int) (relative / sweep));
        return outerActions.get(index);
    }

    private static float normalizeDegrees(float degrees) {
        float normalized = degrees % 360f;
        return normalized < 0f ? normalized + 360f : normalized;
    }

    private Action hitTestWithHysteresis(float x, float y, Action previous) {
        Action candidate = hitTest(x, y);
        if (previous == null || candidate == previous) return candidate;

        float dx = x - centerX;
        float dy = y - centerY;
        float distance = (float) Math.hypot(dx, dy);
        float slop = dp(6f);

        if (candidate == null) {
            return distance <= outerRadius + slop ? previous : null;
        }

        int previousCenter = centerActions.indexOf(previous);
        int candidateCenter = centerActions.indexOf(candidate);
        int previousOuter = outerActions.indexOf(previous);
        int candidateOuter = outerActions.indexOf(candidate);

        if (previousCenter >= 0 && candidateOuter >= 0) {
            return distance >= innerRadius + slop ? candidate : previous;
        }
        if (previousOuter >= 0 && candidateCenter >= 0) {
            return distance <= innerRadius - slop ? candidate : previous;
        }

        if (previousCenter >= 0 && candidateCenter >= 0) {
            float boundary = candidateCenter > previousCenter
                    ? centerBandTop(candidateCenter)
                    : centerBandTop(previousCenter);
            boolean crossed = candidateCenter > previousCenter
                    ? y >= boundary + slop
                    : y <= boundary - slop;
            return crossed ? candidate : previous;
        }

        if (previousOuter >= 0 && candidateOuter >= 0) {
            float sweep = 360f / outerActions.size();
            float candidateCenterAngle = firstOuterActionAngle + candidateOuter * sweep;
            float angle = (float) Math.toDegrees(Math.atan2(dy, dx));
            float difference = Math.abs(normalizeDegrees(angle - candidateCenterAngle + 180f) - 180f);
            float angularSlop = (float) Math.toDegrees(
                    Math.atan2(slop, Math.max(innerRadius, distance)));
            return difference <= sweep * 0.5f - angularSlop ? candidate : previous;
        }

        return candidate;
    }

    private float totalCenterWeight() {
        float total = 0f;
        for (float weight : centerWeights) total += weight;
        return total;
    }

    private float centerBandTop(int index) {
        float preceding = 0f;
        for (int current = 0; current < index; current++) preceding += centerWeights[current];
        return centerY - innerRadius
                + innerRadius * 2f * preceding / totalCenterWeight();
    }

    private float centerBandHeight(int index) {
        return innerRadius * 2f * centerWeights[index] / totalCenterWeight();
    }

    private void updateCapturedPointers(MotionEvent event) {
        SparseArray<Action> nextPointerActions = new SparseArray<>();
        Map<Action, Integer> nextPressCounts = new IdentityHashMap<>();

        for (int capturedIndex = 0; capturedIndex < capturedPointers.size(); capturedIndex++) {
            int pointerId = capturedPointers.keyAt(capturedIndex);
            int eventIndex = event.findPointerIndex(pointerId);
            Action next;
            if (eventIndex >= 0) {
                next = hitTestWithHysteresis(event.getX(eventIndex), event.getY(eventIndex),
                        pointerActions.get(pointerId));
            } else {
                next = pointerActions.get(pointerId);
            }
            if (next != null) {
                nextPointerActions.put(pointerId, next);
                int count = nextPressCounts.containsKey(next) ? nextPressCounts.get(next) : 0;
                nextPressCounts.put(next, count + 1);
            }
        }

        List<Action> released = new ArrayList<>();
        for (Action action : pressCounts.keySet()) {
            if (!nextPressCounts.containsKey(action)) released.add(action);
        }
        List<Action> pressed = new ArrayList<>();
        for (Action action : nextPressCounts.keySet()) {
            if (!pressCounts.containsKey(action)) pressed.add(action);
        }

        pointerActions.clear();
        for (int index = 0; index < nextPointerActions.size(); index++) {
            pointerActions.put(nextPointerActions.keyAt(index), nextPointerActions.valueAt(index));
        }
        pressCounts.clear();
        pressCounts.putAll(nextPressCounts);
        notifyActionsChanged(released, pressed);
        if (!pressed.isEmpty()) performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
        invalidate();
    }

    private void updatePointerAction(int pointerId, Action next, boolean haptic) {
        Action previous = pointerActions.get(pointerId);
        if (previous == next) return;

        List<Action> released = new ArrayList<>(1);
        List<Action> pressed = new ArrayList<>(1);
        if (previous != null && release(previous)) released.add(previous);
        if (next == null) {
            pointerActions.remove(pointerId);
        } else {
            pointerActions.put(pointerId, next);
            if (press(next)) pressed.add(next);
            if (haptic) performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
        }
        notifyActionsChanged(released, pressed);
        invalidate();
    }

    private void notifyActionsChanged(List<Action> released, List<Action> pressed) {
        if (actionListener != null && (!released.isEmpty() || !pressed.isEmpty())) {
            actionListener.onActionsChanged(released, pressed);
        }
    }

    private boolean press(Action action) {
        int count = pressCounts.containsKey(action) ? pressCounts.get(action) : 0;
        pressCounts.put(action, count + 1);
        return count == 0;
    }

    private boolean release(Action action) {
        Integer current = pressCounts.get(action);
        if (current == null) return false;
        if (current <= 1) {
            pressCounts.remove(action);
            return true;
        } else {
            pressCounts.put(action, current - 1);
            return false;
        }
    }

    private boolean isPressed(Action action) {
        Integer count = pressCounts.get(action);
        return count != null && count > 0;
    }

    public void releaseAllActions() {
        List<Action> released = new ArrayList<>(pressCounts.keySet());
        notifyActionsChanged(released, new ArrayList<>());
        pressCounts.clear();
        pointerActions.clear();
        capturedPointers.clear();
        invalidate();
    }

    private void updateContentDescription() {
        StringBuilder description = new StringBuilder();
        for (Action action : centerActions) appendDescription(description, action);
        for (Action action : outerActions) appendDescription(description, action);
        setContentDescription(description.toString());
    }

    private static void appendDescription(StringBuilder description, Action action) {
        if (description.length() > 0) description.append(", ");
        description.append(action.contentDescription);
    }

    @Override
    public boolean performClick() {
        super.performClick();
        return true;
    }

    @Override
    protected void onVisibilityChanged(View changedView, int visibility) {
        super.onVisibilityChanged(changedView, visibility);
        if (visibility != VISIBLE) releaseAllActions();
    }

    @Override
    protected void onDetachedFromWindow() {
        releaseAllActions();
        super.onDetachedFromWindow();
    }

    private float dp(float value) {
        return value * getResources().getDisplayMetrics().density;
    }
}
