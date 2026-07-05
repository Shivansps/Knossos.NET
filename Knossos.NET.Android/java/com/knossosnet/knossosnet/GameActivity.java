package com.knossosnet.knossosnet;
import android.annotation.SuppressLint;
import android.content.Intent;
import android.os.Build;
import android.os.Bundle;
import android.view.WindowManager;
import com.knossosnet.knossosnet.overlay.NativeBridge;
import com.knossosnet.knossosnet.overlay.RadialDpadView;
import com.knossosnet.knossosnet.tts.TTSManager;
import java.util.ArrayList;
import android.view.*;
import android.widget.*;
import java.io.File;
import java.util.Arrays;
import java.util.List;
import java.lang.ref.WeakReference;

public class GameActivity extends org.libsdl.app.SDLActivity {

    private static String _workingFolder = "";
    private static WeakReference<View> _overlayRef = null;
    private static Boolean _pendingVisibility = null;
    private static Boolean _forceOverlayOn = false;

    /* FSO API */

    public static String getWorkingFolder() { return _workingFolder; }

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
            overlay.post(() -> overlay.setVisibility(View.GONE));
        } else {
            _pendingVisibility = false;
        }
    }
	
	// TTS wrappers ----------------------------------------------------------
    public static boolean tts_speak(String text)     { return TTSManager.speak(text); }
    public static boolean tts_stop()                 { return TTSManager.stop(); }
    public static boolean tts_pause()                { return TTSManager.pause(); }
    public static boolean tts_resume()               { return TTSManager.resume(); }
    public static boolean tts_isSpeaking()           { return TTSManager.isSpeaking(); }
    public static void    tts_shutdown()             { TTSManager.shutdown(); }
    public static void    tts_setRate(float rate)    { TTSManager.setRate(rate); }
    public static void    tts_setLanguageTag(String tag) { TTSManager.setLanguageTag(tag); }
    public static String[] tts_getAvailableLanguageTags() { return TTSManager.getAvailableLanguageTags(); }
    // -------------------------------------------------------------------------

    /* ******* */

    @Override
    protected String[] getArguments() {
        android.content.Intent i = getIntent();
        java.util.ArrayList<String> args = (i != null)
                ? i.getStringArrayListExtra("fsoArgs")
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
        return (path == null || path.isEmpty()) ? null : path;
    }

    @Override
    protected String getMainFunction() {
        return "android_main";
    }

    private void hideSystemUI() {
        View decorView = getWindow().getDecorView();
        decorView.setSystemUiVisibility(
                View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                        | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                        | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                        | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                        | View.SYSTEM_UI_FLAG_FULLSCREEN
                        | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) {
            hideSystemUI();
        }
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        //Load .so files from internal private storage in order (fso last)
        File dir = new File(getFilesDir(), "natives/");
        // 1) ordenar
        List<File> loadList = orderForLoad(dir);

        // 2) primera pasada
        List<File> failed = new ArrayList<File>();
        for (int i = 0; i < loadList.size(); i++) {
            File so = loadList.get(i);
            if (!tryLoad(so)) failed.add(so);
        }

        if (!failed.isEmpty()) {
            List<File> still = new ArrayList<File>();
            for (int i = 0; i < failed.size(); i++) {
                if (!tryLoad(failed.get(i))) still.add(failed.get(i));
            }
            if (!still.isEmpty()) {
                for (int i = 0; i < still.size(); i++) {
                    System.err.println("STILL failing: " + still.get(i).getName());
                }
            }
        }

        //Set some settings
        try {
            getWindow().setSustainedPerformanceMode(true);
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        } catch (Throwable ignored) {
        }

        hideSystemUI();

        //Init TTS
        TTSManager.init(this);

        Intent i = getIntent();
        // Set working folder
        if(i != null)
        {
            _workingFolder = i.getStringExtra("workingFolder");
            _forceOverlayOn = i.getBooleanExtra("forceTouchOverlay", false);
        }

        //Start game
        super.onCreate(savedInstanceState);

        //Start the touch overlay? Needs to be done after super.oncreate
		getWindow().getDecorView().post(this::setupOverlayFromXml);
    }

    private static final String[] PREFERRED_ORDER = new String[] {
        "libSDL2.so",
        "libshaderc.so",
        "libopenal.so",
        "libavutil.so",
        "libswresample.so",
        "libswscale.so",
        "libavcodec.so",
        "libavformat.so",
        "libavfilter.so"
    };

    private static List<File> orderForLoad(File dir) {
        ArrayList<File> ordered = new ArrayList<File>();
        if (dir == null || !dir.isDirectory()) return ordered;

        // Add libs in preferred order
        for (int i = 0; i < PREFERRED_ORDER.length; i++) {
            File f = new File(dir, PREFERRED_ORDER[i]);
            if (f.isFile()) ordered.add(f);
        }

        // Add the rest that is not an engine build
        File[] arr = dir.listFiles(new java.io.FilenameFilter() {
            @Override public boolean accept(File d, String name) {
                return name != null && name.endsWith(".so");
            }
        });
        if (arr != null) {
            for (int i = 0; i < arr.length; i++) {
                File f = arr[i];
                if (!containsName(ordered, f.getName()) && !isEngineName(f.getName())) {
                    ordered.add(f);
                }
            }
        }

        // Add only the requested engine build
        /*
        if (arr != null) {
            for (int i = 0; i < arr.length; i++) {
                File f = arr[i];
                if (!containsName(ordered, f.getName()) && isEngineName(f.getName())) {
                    ordered.add(f);
                }
            }
        }
        */
        return ordered;
    }

    private boolean tryLoad(File so) 
    {
        try {
            if (so != null && so.isFile()) {
                System.load(so.getAbsolutePath());
                return true;
            }
        } catch (UnsatisfiedLinkError e) {
            e.printStackTrace();
        }
        return false;
    }

    private static boolean containsName(List<File> list, String name) 
    {
        for (int i = 0; i < list.size(); i++) {
            if (list.get(i).getName().equals(name)) return true;
        }
        return false;
    }

    private static boolean isEngineName(String name) 
    {
        if (name == null) return false;
        return name.startsWith("libfso") || name.contains("libfs2");
    }
   

    @Override protected void onPause() 
    {
        TTSManager.stop();
        super.onPause();
    }

    @Override protected void onResume() 
    {
        super.onResume();
        hideSystemUI();
    }

    @Override protected void onDestroy()
    {
        _workingFolder = "";
        _overlayRef  = null;
        _pendingVisibility = null;
        TTSManager.shutdown();
        super.onDestroy();
        try {
            if (isChangingConfigurations()) return;
            // kill process if it is still running
            // Note: may break pilot files
            String proc = android.app.Application.getProcessName();
            if (proc != null && proc.endsWith(":game")) {
                android.os.Process.killProcess(android.os.Process.myPid());
            }
        } catch (Throwable ignored) {}
    }
	
	private void toggleSdlKeyboard(View overlayRoot) {
        boolean imeVisible = false;

        if (Build.VERSION.SDK_INT >= 30) {
            WindowInsets insets = overlayRoot.getRootWindowInsets();
            if (insets != null) {
                imeVisible = insets.isVisible(WindowInsets.Type.ime());
            }
        }

        if (!imeVisible) {
            NativeBridge.setTextInputEnabled(true);
        } else {
            NativeBridge.setTextInputEnabled(false);
        }
    }

    @SuppressLint("ClickableViewAccessibility")
    private android.view.View.OnTouchListener makeTouchHandler(final int code) {
        return new android.view.View.OnTouchListener() {
            @Override public boolean onTouch(android.view.View v, android.view.MotionEvent e) {
                int action = e.getActionMasked();
                if (action == android.view.MotionEvent.ACTION_DOWN) {
                    v.setPressed(true);
                    NativeBridge.onButton(code, true);
                    return true;
                } else if (action == android.view.MotionEvent.ACTION_UP
                        || action == android.view.MotionEvent.ACTION_CANCEL) {
                    v.setPressed(false);
                    NativeBridge.onButton(code, false);
                    return true;
                }
                return false;
            }
        };
    }


    @SuppressLint({"ClickableViewAccessibility", "DiscouragedApi"})
    private void setupOverlayFromXml()
    {
        int layoutId = getResources().getIdentifier("overlay_controls", "layout", getPackageName());
        View overlay = getLayoutInflater().inflate(layoutId, null);

        Button btnToggle = overlay.findViewById(getResources().getIdentifier("btnToggle", "id", getPackageName()));
        RadialDpadView dpad = overlay.findViewById(getResources().getIdentifier("dpad", "id", getPackageName()));


        // Button listeners
        Button btnKyb = overlay.findViewById(getResources().getIdentifier("btnKyb", "id", getPackageName()));
        btnKyb.setOnClickListener(v -> toggleSdlKeyboard(overlay));
        // ESC
        Button btnEsc = overlay.findViewById(getResources().getIdentifier("btnEsc", "id", getPackageName()));
        btnEsc.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_ESC));

        // F3
        Button btnF3 = overlay.findViewById(getResources().getIdentifier("btnF3", "id", getPackageName()));
        btnF3.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_F3));

        // ALT+J
        Button btnALTJ = overlay.findViewById(getResources().getIdentifier("btnAltJ", "id", getPackageName()));
        btnALTJ.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_ALT_J));

        // ALT+M
        Button btnALTM = overlay.findViewById(getResources().getIdentifier("btnAltM", "id", getPackageName()));
        btnALTM.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_ALT_M));

        // ALT+H
        Button btnALTH = overlay.findViewById(getResources().getIdentifier("btnAltH", "id", getPackageName()));
        btnALTH.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_ALT_H));

        // ALT+A
        Button btnAltA = overlay.findViewById(getResources().getIdentifier("btnAltA", "id", getPackageName()));
        btnAltA.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_ALT_A));

        // Space
        Button btnSpace = overlay.findViewById(getResources().getIdentifier("btnFireS", "id", getPackageName()));
        btnSpace.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_SPACE));

        // LCtrl
        Button btnLCtrl = overlay.findViewById(getResources().getIdentifier("btnFireP", "id", getPackageName()));
        btnLCtrl.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_CTRL));

        // CycleP
        Button btnCycleP = overlay.findViewById(getResources().getIdentifier("btnCycleP", "id", getPackageName()));
        btnCycleP.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_CYCLE_P));

        // CycleS
        Button btnCycleS = overlay.findViewById(getResources().getIdentifier("btnCycleS", "id", getPackageName()));
        btnCycleS.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_CYCLE_S));

        // Tab
        Button btnTab = overlay.findViewById(getResources().getIdentifier("btnTab", "id", getPackageName()));
        btnTab.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_TAB));

        // +
        Button btnPlus = overlay.findViewById(getResources().getIdentifier("btnPlus", "id", getPackageName()));
        btnPlus.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_PLUS));

        // -
        Button btnMinus = overlay.findViewById(getResources().getIdentifier("btnMinus", "id", getPackageName()));
        btnMinus.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_MINUS));

        // Q
        Button btnQ = overlay.findViewById(getResources().getIdentifier("btnQ", "id", getPackageName()));
        btnQ.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_Q));

        // -
        Button btnX = overlay.findViewById(getResources().getIdentifier("btnX", "id", getPackageName()));
        btnX.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_X));

        // Y
        Button btnY = overlay.findViewById(getResources().getIdentifier("btnY", "id", getPackageName()));
        btnY.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_Y));

        // H
        Button btnH = overlay.findViewById(getResources().getIdentifier("btnH", "id", getPackageName()));
        btnH.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_H));

        // B
        Button btnB = overlay.findViewById(getResources().getIdentifier("btnB", "id", getPackageName()));
        btnB.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_B));

        // E
        Button btnE = overlay.findViewById(getResources().getIdentifier("btnE", "id", getPackageName()));
        btnE.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_E));

        // F
        Button btnF = overlay.findViewById(getResources().getIdentifier("btnF", "id", getPackageName()));
        btnF.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_F));

        // T
        Button btnT = overlay.findViewById(getResources().getIdentifier("btnT", "id", getPackageName()));
        btnT.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_T));

        // M
        Button btnM = overlay.findViewById(getResources().getIdentifier("btnM", "id", getPackageName()));
        btnM.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_M));

        // S
        Button btnS = overlay.findViewById(getResources().getIdentifier("btnS", "id", getPackageName()));
        btnS.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_S));

        // A
        Button btnA = overlay.findViewById(getResources().getIdentifier("btnA", "id", getPackageName()));
        btnA.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_A));

        // Z
        Button btnZ = overlay.findViewById(getResources().getIdentifier("btnZ", "id", getPackageName()));
        btnZ.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_Z));

        // Return
        Button btnRet = overlay.findViewById(getResources().getIdentifier("btnRet", "id", getPackageName()));
        btnRet.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_BACKSPACE));

        // Backslash
        Button btnBackSlash = overlay.findViewById(getResources().getIdentifier("btnBackSlash", "id", getPackageName()));
        btnBackSlash.setOnTouchListener(makeTouchHandler(NativeBridge.CODE_KEY_BACKSLASH));

        // C+3+1
        Button btnC31 = overlay.findViewById(getResources().getIdentifier("btnC31", "id", getPackageName()));
        btnC31.setOnClickListener(v -> NativeBridge.runMacro(NativeBridge.C_3_1));

        // C+3+5
        Button btnC35 = overlay.findViewById(getResources().getIdentifier("btnC35", "id", getPackageName()));
        btnC35.setOnClickListener(v -> NativeBridge.runMacro(NativeBridge.C_3_5));

        // C+3+9
        Button btnC39 = overlay.findViewById(getResources().getIdentifier("btnC39", "id", getPackageName()));
        btnC39.setOnClickListener(v -> NativeBridge.runMacro(NativeBridge.C_3_9));

        // C+5
        Button btnC5 = overlay.findViewById(getResources().getIdentifier("btnC5", "id", getPackageName()));
        btnC5.setOnClickListener(v -> NativeBridge.runMacro(NativeBridge.C_5));

        // Buttons that visibility are controlled by the toggle
        View[] topBar = new View[] {
                btnEsc, btnF3, btnALTJ, btnALTM, btnALTH, btnAltA, btnC31, btnC35, btnC39, btnC5, btnKyb };

        View[] joystick = new View[] {
                dpad, btnSpace, btnLCtrl, btnCycleP, btnCycleS, btnTab, btnS, btnA, btnZ, btnRet,
                btnPlus, btnMinus, btnX, btnQ, btnY, btnH, btnB, btnE, btnF, btnT, btnM, btnBackSlash };

        btnToggle.setOnClickListener(v -> {
            boolean topBarVisible = topBar[0].getVisibility() == View.VISIBLE;
            boolean joystickVisible = joystick[0].getVisibility() == View.VISIBLE;

            int newTop = (topBarVisible && joystickVisible) ? View.GONE : View.VISIBLE;
            int newJoy = (topBarVisible && !joystickVisible) ? View.VISIBLE : View.GONE;

            for (int i = 0; i < topBar.length; i++) topBar[i].setVisibility(newTop);
            for (int i = 0; i < joystick.length; i++) joystick[i].setVisibility(newJoy);
        });

        for (View w : topBar) w.setVisibility(
                View.GONE
        );
        for (View w : joystick) w.setVisibility(
                View.GONE
        );

        FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT);
        addContentView(overlay, lp);
        overlay.bringToFront();
        overlay.setElevation(10000f);


        if (Build.VERSION.SDK_INT >= 30) {
            final WindowInsetsController c = getWindow().getInsetsController();
            if (c != null) {
                c.hide(WindowInsets.Type.systemBars());
                c.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            }
        }

        _overlayRef = new WeakReference<>(overlay);

        if(!_forceOverlayOn)
            overlay.setVisibility(View.GONE);

        if (_pendingVisibility != null) {
            overlay.setVisibility(_pendingVisibility ? View.VISIBLE : View.GONE);
            _pendingVisibility = null;
        }
    }
}