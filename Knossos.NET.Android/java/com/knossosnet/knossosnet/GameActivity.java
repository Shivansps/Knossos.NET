package com.knossosnet.knossosnet;

import android.annotation.SuppressLint;
import android.content.Intent;
import android.graphics.Insets;
import android.os.Build;
import android.os.Bundle;
import android.util.SparseIntArray;
import android.view.DisplayCutout;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.HorizontalScrollView;

import com.knossosnet.knossosnet.overlay.HudStyle;
import com.knossosnet.knossosnet.overlay.NativeBridge;
import com.knossosnet.knossosnet.overlay.RadialActionView;
import com.knossosnet.knossosnet.overlay.RadialDpadView;
import com.knossosnet.knossosnet.tts.TTSManager;

import java.io.File;
import java.lang.ref.WeakReference;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

public class GameActivity extends org.libsdl.app.SDLActivity {

    private static String _workingFolder = "";
    private static WeakReference<View> _overlayRef = null;
    private static WeakReference<GameActivity> _activityRef = null;
    private static Boolean _pendingVisibility = null;
    private static Boolean _forceOverlayOn = false;

    private final SparseIntArray heldKeyCounts = new SparseIntArray();
    private final ArrayList<RadialActionView> radialControls = new ArrayList<>();
    private RadialDpadView dpadControl;
    private Button[] hudButtons = new Button[0];
    private View[] topControls = new View[0];
    private View[] topBarTouchContainers = new View[0];
    private View[] levelOneControls = new View[0];
    private View[] levelTwoControls = new View[0];
    private View[] levelThreeControls = new View[0];
    private int hudMode = 0;

    /* FSO API */

    public static String getWorkingFolder() {
        return _workingFolder;
    }

    public static void setOverlayOpacity(float opacity) {
        HudStyle.setBackgroundOpacity(opacity);
        GameActivity activity = _activityRef != null ? _activityRef.get() : null;
        View overlay = _overlayRef != null ? _overlayRef.get() : null;
        if (activity != null && overlay != null) {
            overlay.post(activity::refreshHudAppearance);
        }
    }

    public static void enableOverlay() {
        if (_forceOverlayOn) return;
        View overlay = _overlayRef != null ? _overlayRef.get() : null;
        if (overlay != null) {
            overlay.post(() -> overlay.setVisibility(View.VISIBLE));
        } else {
            _pendingVisibility = true;
        }
    }

    public static void disableOverlay() {
        if (_forceOverlayOn) return;
        View overlay = _overlayRef != null ? _overlayRef.get() : null;
        if (overlay != null) {
            GameActivity activity = _activityRef != null ? _activityRef.get() : null;
            overlay.post(() -> {
                if (activity != null) activity.releaseOverlayInputs();
                overlay.setVisibility(View.GONE);
            });
        } else {
            _pendingVisibility = false;
        }
    }

    // TTS wrappers ----------------------------------------------------------
    public static boolean tts_speak(String text) { return TTSManager.speak(text); }
    public static boolean tts_stop() { return TTSManager.stop(); }
    public static boolean tts_pause() { return TTSManager.pause(); }
    public static boolean tts_resume() { return TTSManager.resume(); }
    public static boolean tts_isSpeaking() { return TTSManager.isSpeaking(); }
    public static void tts_shutdown() { TTSManager.shutdown(); }
    public static void tts_setRate(float rate) { TTSManager.setRate(rate); }
    public static void tts_setLanguageTag(String tag) { TTSManager.setLanguageTag(tag); }
    public static String[] tts_getAvailableLanguageTags() {
        return TTSManager.getAvailableLanguageTags();
    }
    // ----------------------------------------------------------------------

    @Override
    protected String[] getArguments() {
        Intent intent = getIntent();
        ArrayList<String> args = intent != null
                ? intent.getStringArrayListExtra("fsoArgs")
                : null;

        if (args == null || args.isEmpty()) {
            return new String[0];
        }
        return args.toArray(new String[0]);
    }

    @Override
    protected String[] getLibraries() {
        return new String[] { };
    }

    @Override
    protected String getMainSharedObject() {
        String path = getIntent().getStringExtra("engineLibName");
        return path == null || path.isEmpty() ? null : path;
    }

    @Override
    protected String getMainFunction() {
        return "android_main";
    }

    private void hideSystemUI() {
        Window window = getWindow();
        View decorView = window.getDecorView();

        if (Build.VERSION.SDK_INT >= 30) {
            window.setDecorFitsSystemWindows(false);
            WindowInsetsController controller = window.getInsetsController();
            if (controller != null) {
                controller.hide(WindowInsets.Type.systemBars());
                controller.setSystemBarsBehavior(
                        WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            }
        } else {
            decorView.setSystemUiVisibility(
                    View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                            | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                            | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                            | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                            | View.SYSTEM_UI_FLAG_FULLSCREEN
                            | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
        }
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) hideSystemUI();
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        // Load .so files from internal private storage in order (FSO last).
        File dir = new File(getFilesDir(), "natives/");
        List<File> loadList = orderForLoad(dir);

        List<File> failed = new ArrayList<>();
        for (File so : loadList) {
            if (!tryLoad(so)) failed.add(so);
        }

        if (!failed.isEmpty()) {
            List<File> still = new ArrayList<>();
            for (File so : failed) {
                if (!tryLoad(so)) still.add(so);
            }
            for (File so : still) {
                System.err.println("STILL failing: " + so.getName());
            }
        }

        try {
            getWindow().setSustainedPerformanceMode(true);
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        } catch (Throwable ignored) {
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            getWindow().getAttributes().layoutInDisplayCutoutMode =
                    WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
        }

        hideSystemUI();
        TTSManager.init(this);

        Intent intent = getIntent();
        if (intent != null) {
            _workingFolder = intent.getStringExtra("workingFolder");
            _forceOverlayOn = intent.getBooleanExtra("forceTouchOverlay", false);
        }

        super.onCreate(savedInstanceState);

        _activityRef = new WeakReference<>(this);
        if (_forceOverlayOn) HudStyle.ensureVisible();

        // SDL's content view is created by super.onCreate().
        getWindow().getDecorView().post(this::setupOverlayFromXml);
    }

    private static final String[] PREFERRED_ORDER = new String[] {
            "libSDL3.so",
            "libshaderc_shared.so",
            "libopenal.so",
            "libavutil.so",
            "libswresample.so",
            "libswscale.so",
            "libavcodec.so",
            "libavformat.so",
            "libavfilter.so"
    };

    private static List<File> orderForLoad(File dir) {
        ArrayList<File> ordered = new ArrayList<>();
        if (dir == null || !dir.isDirectory()) return ordered;

        for (String name : PREFERRED_ORDER) {
            File file = new File(dir, name);
            if (file.isFile()) ordered.add(file);
        }

        File[] files = dir.listFiles((directory, name) ->
                name != null && name.endsWith(".so"));
        if (files != null) {
            for (File file : files) {
                if (!containsName(ordered, file.getName()) && !isEngineName(file.getName())) {
                    ordered.add(file);
                }
            }
        }
        return ordered;
    }

    private boolean tryLoad(File so) {
        try {
            String name = so.getName();
            if (name.equals("libSDL2.so")) return true;
            if (so.isFile()) {
                System.load(so.getAbsolutePath());
                return true;
            }
        } catch (UnsatisfiedLinkError error) {
            error.printStackTrace();
        }
        return false;
    }

    private static boolean containsName(List<File> list, String name) {
        for (File file : list) {
            if (file.getName().equals(name)) return true;
        }
        return false;
    }

    private static boolean isEngineName(String name) {
        return name != null && (name.startsWith("libfso") || name.contains("libfs2"));
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        int keyCode = event.getKeyCode();
        if (keyCode == KeyEvent.KEYCODE_ESCAPE || keyCode == KeyEvent.KEYCODE_BACK) {
            switch (event.getAction()) {
                case KeyEvent.ACTION_DOWN:
                    if (event.getRepeatCount() == 0) {
                        setKeyPressed(NativeBridge.CODE_ESC, true);
                    }
                    return true;
                case KeyEvent.ACTION_UP:
                    setKeyPressed(NativeBridge.CODE_ESC, false);
                    return true;
            }
            return true;
        }
        return super.dispatchKeyEvent(event);
    }

    @Override
    protected void onPause() {
        releaseOverlayInputs();
        TTSManager.stop();
        super.onPause();
    }

    @Override
    protected void onResume() {
        super.onResume();
        hideSystemUI();
    }

    @Override
    protected void onDestroy() {
        releaseOverlayInputs();
        _workingFolder = "";
        _overlayRef = null;
        _activityRef = null;
        _pendingVisibility = null;
        TTSManager.shutdown();
        super.onDestroy();
        try {
            if (isChangingConfigurations()) return;
            // Kill the isolated game process if it is still running.
            // Note: this may break pilot files.
            String process = android.app.Application.getProcessName();
            if (process != null && process.endsWith(":game")) {
                android.os.Process.killProcess(android.os.Process.myPid());
            }
        } catch (Throwable ignored) {
        }
    }

    private void toggleSdlKeyboard(View overlayRoot) {
        boolean imeVisible = false;

        if (Build.VERSION.SDK_INT >= 30) {
            WindowInsets insets = overlayRoot.getRootWindowInsets();
            if (insets != null) {
                imeVisible = insets.isVisible(WindowInsets.Type.ime());
            }
        }

        NativeBridge.setTextInputEnabled(!imeVisible);
    }

    @SuppressLint("ClickableViewAccessibility")
    private View.OnTouchListener makeTouchHandler(int... codes) {
        final int[] actionCodes = Arrays.copyOf(codes, codes.length);
        return (view, event) -> {
            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    view.setPressed(true);
                    setKeysPressed(actionCodes, true);
                    return true;
                case MotionEvent.ACTION_MOVE:
                    return true;
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL:
                    view.setPressed(false);
                    setKeysPressed(actionCodes, false);
                    return true;
            }
            return false;
        };
    }

    @SuppressLint("ClickableViewAccessibility")
    private void bindHoldButton(Button button, int... codes) {
        final int[] actionCodes = Arrays.copyOf(codes, codes.length);
        button.setOnTouchListener(makeTouchHandler(actionCodes));
        button.setOnClickListener(view -> {
            setKeysPressed(actionCodes, true);
            view.postDelayed(() -> setKeysPressed(actionCodes, false), 45L);
        });
    }

    private void setKeysPressed(int[] codes, boolean pressed) {
        if (pressed) {
            for (int code : codes) setKeyPressed(code, true);
        } else {
            for (int index = codes.length - 1; index >= 0; index--) {
                setKeyPressed(codes[index], false);
            }
        }
    }

    private void setKeyPressed(int code, boolean pressed) {
        int count = heldKeyCounts.get(code, 0);
        if (pressed) {
            heldKeyCounts.put(code, count + 1);
            if (count == 0) NativeBridge.onButton(code, true);
        } else if (count > 0) {
            if (count == 1) {
                heldKeyCounts.delete(code);
                NativeBridge.onButton(code, false);
            } else {
                heldKeyCounts.put(code, count - 1);
            }
        }
    }

    private void dispatchRadialTransition(List<RadialActionView.Action> releasedActions,
                                          List<RadialActionView.Action> pressedActions) {
        SparseIntArray deltas = new SparseIntArray();
        for (RadialActionView.Action releasedAction : releasedActions) {
            for (int code : releasedAction.getKeyCodes()) {
                deltas.put(code, deltas.get(code, 0) - 1);
            }
        }
        for (RadialActionView.Action pressedAction : pressedActions) {
            for (int code : pressedAction.getKeyCodes()) {
                deltas.put(code, deltas.get(code, 0) + 1);
            }
        }

        // Shared keys remain held while sliding between composite actions.
        for (int index = 0; index < deltas.size(); index++) {
            int delta = deltas.valueAt(index);
            for (int count = 0; count < -delta; count++) {
                setKeyPressed(deltas.keyAt(index), false);
            }
        }
        for (int index = 0; index < deltas.size(); index++) {
            int delta = deltas.valueAt(index);
            for (int count = 0; count < delta; count++) {
                setKeyPressed(deltas.keyAt(index), true);
            }
        }
    }

    private void releaseOverlayInputs() {
        for (RadialActionView control : radialControls) control.releaseAllActions();
        if (dpadControl != null) dpadControl.releaseAllControls();
        for (View control : topControls) control.setPressed(false);
        while (heldKeyCounts.size() > 0) {
            int code = heldKeyCounts.keyAt(heldKeyCounts.size() - 1);
            heldKeyCounts.removeAt(heldKeyCounts.size() - 1);
            NativeBridge.onButton(code, false);
        }
    }

    private void refreshHudAppearance() {
        for (Button button : hudButtons) HudStyle.applyTo(button);
        for (RadialActionView control : radialControls) control.invalidate();
        if (dpadControl != null) dpadControl.invalidate();
    }

    private RadialActionView.Action action(String label, int... codes) {
        return new RadialActionView.Action(label, codes);
    }

    private RadialActionView.Action action(String label, String description, int... codes) {
        return new RadialActionView.Action(label, description, codes);
    }

    private void configureWheel(RadialActionView wheel,
                                RadialActionView.Action[] center,
                                RadialActionView.Action[] outer) {
        wheel.setOnActionListener(this::dispatchRadialTransition);
        wheel.setActions(Arrays.asList(center), Arrays.asList(outer));
        radialControls.add(wheel);
    }

    private void applyHudMode() {
        boolean showTopButtons = hudMode >= 1;
        for (View container : topBarTouchContainers) {
            // INVISIBLE keeps both weighted halves in the layout, so the mode
            // button remains centered without letting empty scrollers eat touch.
            container.setVisibility(showTopButtons ? View.VISIBLE : View.INVISIBLE);
        }
        setControlGroupVisible(levelOneControls, showTopButtons);
        setControlGroupVisible(levelTwoControls, hudMode >= 2);
        setControlGroupVisible(levelThreeControls, hudMode >= 3);
    }

    private static void setControlGroupVisible(View[] controls, boolean visible) {
        for (View control : controls) {
            control.setVisibility(visible ? View.VISIBLE : View.GONE);
        }
    }

    @SuppressLint("DiscouragedApi")
    private int requireResourceId(String name, String type) {
        int id = getResources().getIdentifier(name, type, getPackageName());
        if (id == 0) {
            throw new IllegalStateException("Missing Android resource " + type + "/" + name);
        }
        return id;
    }

    private <T extends View> T requireOverlayView(View root, String name, Class<T> type) {
        View view = root.findViewById(requireResourceId(name, "id"));
        if (!type.isInstance(view)) {
            throw new IllegalStateException("Missing or invalid overlay view: " + name);
        }
        return type.cast(view);
    }

    private int dp(float value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private void updateOverlayInsets(View overlay, WindowInsets insets) {
        int left = 0;
        int right = 0;
        int bottom = 0;

        if (Build.VERSION.SDK_INT >= 29) {
            Insets gestures = insets.getSystemGestureInsets();
            left = Math.max(left, gestures.left);
            right = Math.max(right, gestures.right);
            bottom = Math.max(bottom, gestures.bottom);
        } else {
            left = Math.max(left, insets.getSystemWindowInsetLeft());
            right = Math.max(right, insets.getSystemWindowInsetRight());
            bottom = Math.max(bottom, insets.getSystemWindowInsetBottom());
        }

        if (Build.VERSION.SDK_INT >= 28) {
            DisplayCutout cutout = insets.getDisplayCutout();
            if (cutout != null) {
                left = Math.max(left, cutout.getSafeInsetLeft());
                right = Math.max(right, cutout.getSafeInsetRight());
                bottom = Math.max(bottom, cutout.getSafeInsetBottom());
            }
        }

        if (overlay.getPaddingLeft() != left
                || overlay.getPaddingRight() != right
                || overlay.getPaddingBottom() != bottom) {
            overlay.setPadding(left, 0, right, bottom);
        }
    }

    private void layoutRadialControls(View overlay,
                                      RadialActionView communicationWheel,
                                      RadialActionView targetWheel,
                                      RadialDpadView dpad,
                                      RadialActionView weaponWheel) {
        int contentWidth = overlay.getWidth()
                - overlay.getPaddingLeft() - overlay.getPaddingRight();
        int contentHeight = overlay.getHeight()
                - overlay.getPaddingTop() - overlay.getPaddingBottom();
        if (contentWidth <= 0 || contentHeight <= 0) return;

        int topBarHeight = dp(32f);
        int availableHeight = Math.max(1, contentHeight - topBarHeight);
        positionRadialControl(communicationWheel, contentWidth, availableHeight,
                topBarHeight, 0.24f, 140f, 0.10f, 0.06f, 0f);
        positionRadialControl(targetWheel, contentWidth, availableHeight,
                topBarHeight, 0.27f, 160f, 0.86f, 0.06f, 0f);
        positionRadialControl(dpad, contentWidth, availableHeight,
                topBarHeight, 0.58f, 330f, 0.01f, 1f, 6f);
        positionRadialControl(weaponWheel, contentWidth, availableHeight,
                topBarHeight, 0.50f, 280f, 0.91f, 1f, 8f);
    }

    private void positionRadialControl(View control,
                                       int contentWidth,
                                       int availableHeight,
                                       int topBarHeight,
                                       float heightFraction,
                                       float maxSizeDp,
                                       float horizontalBias,
                                       float verticalBias,
                                       float bottomMarginDp) {
        int size = Math.max(1, Math.min(dp(maxSizeDp),
                Math.round(availableHeight * heightFraction)));
        int bottomMargin = dp(bottomMarginDp);
        int left = Math.round(Math.max(0, contentWidth - size) * horizontalBias);
        int top = topBarHeight + Math.round(
                Math.max(0, availableHeight - size - bottomMargin) * verticalBias);

        FrameLayout.LayoutParams params = (FrameLayout.LayoutParams) control.getLayoutParams();
        if (params.width == size && params.height == size
                && params.leftMargin == left && params.topMargin == top
                && params.gravity == (Gravity.TOP | Gravity.START)) {
            return;
        }

        params.width = size;
        params.height = size;
        params.leftMargin = left;
        params.topMargin = top;
        params.rightMargin = 0;
        params.bottomMargin = 0;
        params.gravity = Gravity.TOP | Gravity.START;
        control.setLayoutParams(params);
    }

    @SuppressLint({"ClickableViewAccessibility", "DiscouragedApi"})
    private void setupOverlayFromXml() {
        ViewGroup contentRoot = findViewById(android.R.id.content);
        int layoutId = requireResourceId("overlay_controls", "layout");
        View overlay = getLayoutInflater().inflate(layoutId, contentRoot, false);

        Button btnToggle = requireOverlayView(overlay, "btnToggle", Button.class);
        Button btnKyb = requireOverlayView(overlay, "btnKyb", Button.class);
        btnKyb.setOnClickListener(view -> toggleSdlKeyboard(overlay));

        Button btn0 = requireOverlayView(overlay, "btn0", Button.class);
        Button btnF1 = requireOverlayView(overlay, "btnF1", Button.class);
        Button btnF2 = requireOverlayView(overlay, "btnF2", Button.class);
        Button btnF3 = requireOverlayView(overlay, "btnF3", Button.class);
        Button btnF4 = requireOverlayView(overlay, "btnF4", Button.class);
        Button btnEsc = requireOverlayView(overlay, "btnEsc", Button.class);
        Button btnAltJ = requireOverlayView(overlay, "btnAltJ", Button.class);
        Button btnAltM = requireOverlayView(overlay, "btnAltM", Button.class);
        Button btnAltH = requireOverlayView(overlay, "btnAltH", Button.class);
        Button btnAltA = requireOverlayView(overlay, "btnAltA", Button.class);

        hudButtons = new Button[] {
                btn0, btnF1, btnF2, btnF3, btnF4, btnEsc, btnToggle, btnKyb,
                btnAltJ, btnAltM, btnAltH, btnAltA
        };
        refreshHudAppearance();

        bindHoldButton(btn0, NativeBridge.CODE_KEY_0);
        bindHoldButton(btnF1, NativeBridge.CODE_F1);
        bindHoldButton(btnF2, NativeBridge.CODE_F2);
        bindHoldButton(btnF3, NativeBridge.CODE_F3);
        bindHoldButton(btnF4, NativeBridge.CODE_F4);
        bindHoldButton(btnEsc, NativeBridge.CODE_ESC);
        bindHoldButton(btnAltJ, NativeBridge.CODE_KEY_ALT, NativeBridge.CODE_KEY_J);
        bindHoldButton(btnAltM, NativeBridge.CODE_KEY_ALT, NativeBridge.CODE_KEY_M);
        bindHoldButton(btnAltH, NativeBridge.CODE_KEY_ALT, NativeBridge.CODE_KEY_H);
        bindHoldButton(btnAltA, NativeBridge.CODE_KEY_ALT, NativeBridge.CODE_KEY_A);

        RadialActionView communicationWheel = requireOverlayView(
                overlay, "communicationWheel", RadialActionView.class);
        configureWheel(communicationWheel,
                new RadialActionView.Action[] {
                        action("C", NativeBridge.CODE_KEY_C)
                },
                new RadialActionView.Action[] {
                        action("1", NativeBridge.CODE_KEY_1),
                        action("2", NativeBridge.CODE_KEY_2),
                        action("3", NativeBridge.CODE_KEY_3),
                        action("4", NativeBridge.CODE_KEY_4),
                        action("5", NativeBridge.CODE_KEY_5),
                        action("6", NativeBridge.CODE_KEY_6),
                        action("7", NativeBridge.CODE_KEY_7),
                        action("8", NativeBridge.CODE_KEY_8),
                        action("9", NativeBridge.CODE_KEY_9)
                });
        communicationWheel.setInnerRadiusRatio(0.48f);
        communicationWheel.setFirstOuterActionAngle(-70f);
        communicationWheel.setLabelScale(0.92f);

        RadialActionView targetWheel = requireOverlayView(
                overlay, "targetWheel", RadialActionView.class);
        configureWheel(targetWheel,
                new RadialActionView.Action[] {
                        action("B", NativeBridge.CODE_KEY_B),
                        action("H", NativeBridge.CODE_KEY_H)
                },
                new RadialActionView.Action[] {
                        action("Y", NativeBridge.CODE_KEY_Y),
                        action("E", NativeBridge.CODE_KEY_E),
                        action("S", NativeBridge.CODE_KEY_S),
                        action("T", NativeBridge.CODE_KEY_T),
                        action("F", NativeBridge.CODE_KEY_F)
                });
        targetWheel.setInnerRadiusRatio(0.55f);

        RadialActionView weaponWheel = requireOverlayView(
                overlay, "weaponWheel", RadialActionView.class);
        configureWheel(weaponWheel,
                new RadialActionView.Action[] {
                        action("PRIMARY", "Fire primary", NativeBridge.CODE_KEY_CTRL),
                        action("BOTH", "Fire primary and secondary",
                                NativeBridge.CODE_KEY_CTRL, NativeBridge.CODE_KEY_SPACE),
                        action("SECONDARY", "Fire secondary", NativeBridge.CODE_KEY_SPACE)
                },
                new RadialActionView.Action[] {
                        action("+", NativeBridge.CODE_KEY_PLUS),
                        action("-", NativeBridge.CODE_KEY_MINUS),
                        action("Z", NativeBridge.CODE_KEY_Z),
                        action("X", NativeBridge.CODE_KEY_X),
                        action("Q", NativeBridge.CODE_KEY_Q),
                        action("SW\nS", "Switch secondary weapon", NativeBridge.CODE_KEY_CYCLE_S),
                        action("SW\nP", "Switch primary weapon", NativeBridge.CODE_KEY_CYCLE_P),
                        action("\\", "Backslash", NativeBridge.CODE_KEY_BACKSLASH),
                        action("\u2190", "Backspace", NativeBridge.CODE_KEY_BACKSPACE),
                        action("M", NativeBridge.CODE_KEY_M),
                        action("A", NativeBridge.CODE_KEY_A)
                });
        weaponWheel.setInnerRadiusRatio(0.59f);
        weaponWheel.setCenterWeights(0.42f, 0.16f, 0.42f);
        weaponWheel.setFirstOuterActionAngle(-106.36f);

        dpadControl = requireOverlayView(overlay, "dpad", RadialDpadView.class);
        dpadControl.setOnRingActionListener(
                pressed -> setKeyPressed(NativeBridge.CODE_KEY_TAB, pressed));

        topControls = new View[] {
                btn0, btnF1, btnF2, btnF3, btnF4, btnEsc, btnKyb,
                btnAltJ, btnAltM, btnAltH, btnAltA
        };
        levelOneControls = new View[] {
                btnF1, btnF2, btnF3, btnF4, btnEsc, btnKyb,
                btnAltJ, btnAltM, btnAltH, btnAltA
        };
        levelTwoControls = new View[] {
                btn0, communicationWheel
        };
        levelThreeControls = new View[] {
                targetWheel, weaponWheel, dpadControl
        };

        HorizontalScrollView leftScroller = requireOverlayView(
                overlay, "topBarLeftScroller", HorizontalScrollView.class);
        HorizontalScrollView rightScroller = requireOverlayView(
                overlay, "topBarRightScroller", HorizontalScrollView.class);
        topBarTouchContainers = new View[] { leftScroller, rightScroller };
        btnToggle.setOnClickListener(view -> {
            releaseOverlayInputs();
            hudMode = (hudMode + 1) % 4;
            applyHudMode();
            leftScroller.post(() -> leftScroller.fullScroll(View.FOCUS_RIGHT));
        });
        applyHudMode();

        Runnable relayout = () -> layoutRadialControls(
                overlay, communicationWheel, targetWheel, dpadControl, weaponWheel);
        overlay.setOnApplyWindowInsetsListener((view, insets) -> {
            updateOverlayInsets(view, insets);
            view.post(relayout);
            return insets;
        });
        overlay.addOnLayoutChangeListener((view, left, top, right, bottom,
                                           oldLeft, oldTop, oldRight, oldBottom) -> {
            if (right - left != oldRight - oldLeft || bottom - top != oldBottom - oldTop) {
                relayout.run();
            }
        });

        FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT);
        addContentView(overlay, params);
        overlay.requestApplyInsets();
        overlay.post(relayout);
        leftScroller.post(() -> leftScroller.fullScroll(View.FOCUS_RIGHT));

        overlay.bringToFront();
        overlay.setElevation(10000f);

        hideSystemUI();

        _overlayRef = new WeakReference<>(overlay);

        if (!_forceOverlayOn) overlay.setVisibility(View.GONE);

        if (_pendingVisibility != null) {
            overlay.setVisibility(_pendingVisibility ? View.VISIBLE : View.GONE);
            _pendingVisibility = null;
        }
    }
}
