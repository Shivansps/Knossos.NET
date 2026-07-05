package com.knossosnet.knossosnet.tts;

import android.content.Context;
import android.media.AudioAttributes;
import android.os.Bundle;
import android.speech.tts.TextToSpeech;
import android.speech.tts.UtteranceProgressListener;
import android.speech.tts.Voice;
import java.util.ArrayList;
import java.util.Locale;
import java.util.Set;
import android.app.Activity;

public final class TTSManager implements TextToSpeech.OnInitListener {
    private static final String defaultLangTag = "en-US";
    private static final float defaultRate  = 1.0f;
    private static final float defaultPitch = 1.0f;
    private static volatile TextToSpeech tts;
    private static volatile boolean ready;
    private static volatile boolean speaking;
    private static String _pendingSetVoice = null;
    private static float _pendingSetRate = -1;

    private TTSManager() {}

    public static void init(Context context) {
        if (tts != null) return;
        tts = new TextToSpeech(context, new TTSManager());
    }

    public static void init(Activity activity) {
        init(activity.getApplicationContext());
    }

    @Override
    public void onInit(int status) {
        ready = (status == TextToSpeech.SUCCESS);
        if (!ready) return;

        tts.setAudioAttributes(new AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_MEDIA)
                .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                .build());

        tts.setLanguage(Locale.forLanguageTag(defaultLangTag));
        tts.setSpeechRate(defaultRate);
        tts.setPitch(defaultPitch);

        tts.setOnUtteranceProgressListener(new UtteranceProgressListener() {
            @Override public void onStart(String utteranceId) { speaking = true; }
            @Override public void onDone(String utteranceId)  { speaking = false; }
            @Override public void onError(String utteranceId) { speaking = false; }
        });
        
        if(_pendingSetVoice != null)
        {
            setLanguageTag(_pendingSetVoice);
            _pendingSetVoice = null;
        }
        if(_pendingSetRate != -1) {
            setRate(_pendingSetRate);
            _pendingSetRate = -1;
        }
    }

    public static boolean speak(String text) {
        TextToSpeech engine = tts;
        if (engine == null || !ready) return false;

        String id = String.valueOf(System.nanoTime());

        Bundle params = new Bundle();
        int res = engine.speak(text != null ? text : "", TextToSpeech.QUEUE_FLUSH, params, id);
        return res == TextToSpeech.SUCCESS;
    }

    public static boolean stop() {
        TextToSpeech engine = tts;
        if (engine == null) return false;
        return engine.stop() == TextToSpeech.SUCCESS;
    }

    public static boolean pause() { return stop(); }
    public static boolean resume() { return false; }
    public static boolean isSpeaking() { return speaking; }

    public static void setRate(float rate) { 
        if(!ready) {
            _pendingSetRate = rate;
            return;
        }
        if (tts != null) 
            tts.setSpeechRate(rate); 
    }

	public static void setLanguageTag(String voiceName) {
        if(!ready) {
            _pendingSetVoice = voiceName;
            return;
        }
		TextToSpeech engine = tts;
		if (engine == null || !ready || voiceName == null || voiceName.isEmpty()) return;

		Set<Voice> voices = engine.getVoices();
		if (voices == null) return;

		for (Voice v : voices) {
			if (v != null && voiceName.equals(v.getName())) {
				engine.setVoice(v);
				return;
			}
		}
	}

	public static String[] getAvailableLanguageTags() {
		TextToSpeech engine = tts;
		if (engine == null || !ready) return new String[0];

		Set<Voice> voices = engine.getVoices();
		if (voices == null || voices.isEmpty()) return new String[0];

		ArrayList<Voice> usable = new ArrayList<>();
		for (Voice v : voices) {
			if (v == null) continue;
			Set<String> feats = v.getFeatures();
			boolean notInstalled = feats != null
					&& feats.contains(TextToSpeech.Engine.KEY_FEATURE_NOT_INSTALLED);
			if (notInstalled) continue; // skip non-installed                 
			if (v.isNetworkConnectionRequired()) continue; // skip online voices
			usable.add(v);
		}

		// List english voices first
		usable.sort((a, b) -> {
            boolean aEn = "en".equals(a.getLocale().getLanguage());
            boolean bEn = "en".equals(b.getLocale().getLanguage());
            if (aEn != bEn) return aEn ? -1 : 1;
            int byTag = a.getLocale().toLanguageTag().compareTo(b.getLocale().toLanguageTag());
            if (byTag != 0) return byTag;
            return a.getName().compareTo(b.getName());
        });

		ArrayList<String> out = new ArrayList<>(usable.size());
		for (Voice v : usable) out.add(v.getName());
		return out.toArray(new String[0]);
	}

    public static void shutdown() {
        TextToSpeech engine = tts;
        if (engine != null) {
            engine.shutdown();
            tts = null;
            ready = false;
            speaking = false;
        }
    }
}