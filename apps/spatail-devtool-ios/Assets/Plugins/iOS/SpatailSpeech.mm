// Spatail dev tool: on-device dictation for the prompt field.
//
// A thin wrapper around SFSpeechRecognizer + AVAudioEngine. Unity calls SpatailSpeech_Start with a callback and
// receives partial transcripts while the user speaks, then one final transcript. Listening ends by itself after a
// short silence, or when SpatailSpeech_Stop is called.
//
// Requires NSSpeechRecognitionUsageDescription and NSMicrophoneUsageDescription in Info.plist and Speech.framework
// linked into UnityFramework; the dev tool's Unity post-process build step adds all three.

#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <Speech/Speech.h>

#if !__has_feature(objc_arc)
#error SpatailSpeech.mm relies on ARC. Compile it with -fobjc-arc (the dev tool's post-process build step sets this flag).
#endif

// kind: 0 = partial transcript, 1 = final transcript, 2 = error (text is the message), 3 = stopped without a result
typedef void (*SpatailSpeechCallback)(int kind, const char* text);

static const NSTimeInterval kSilenceTimeout = 1.4;

static SFSpeechRecognizer* gRecognizer = nil;
static AVAudioEngine* gEngine = nil;
static SFSpeechAudioBufferRecognitionRequest* gRequest = nil;
static SFSpeechRecognitionTask* gTask = nil;
static SpatailSpeechCallback gCallback = NULL;
static BOOL gListening = NO;
static BOOL gTapInstalled = NO;
static BOOL gDeliveredFinal = NO;
static NSInteger gGeneration = 0;
static NSString* gLastText = nil;

static void SpatailSpeech_Emit(int kind, NSString* text)
{
    SpatailSpeechCallback callback = gCallback;
    if (callback == NULL)
        return;
    callback(kind, text != nil ? [text UTF8String] : "");
}

// Stops audio capture and releases the recognition objects. Safe to call repeatedly.
static void SpatailSpeech_TearDown(void)
{
    gGeneration++;
    if (gEngine != nil)
    {
        if (gEngine.isRunning)
            [gEngine stop];
        if (gTapInstalled)
        {
            [gEngine.inputNode removeTapOnBus:0];
            gTapInstalled = NO;
        }
    }
    if (gRequest != nil)
    {
        [gRequest endAudio];
        gRequest = nil;
    }
    gTask = nil;
    gListening = NO;
}

static void SpatailSpeech_Fail(NSString* message)
{
    SpatailSpeech_TearDown();
    SpatailSpeech_Emit(2, message);
}

// Ends the utterance when no new words have arrived for a moment.
static void SpatailSpeech_ArmSilenceTimer(void)
{
    NSInteger generation = ++gGeneration;
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(kSilenceTimeout * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        if (generation != gGeneration || !gListening)
            return;
        // Closing the audio makes the recogniser deliver its final result.
        if (gEngine != nil && gEngine.isRunning)
            [gEngine stop];
        if (gRequest != nil)
            [gRequest endAudio];
    });
}

static void SpatailSpeech_Begin(NSString* localeIdentifier)
{
    if (gListening)
        return;

    NSLocale* locale = localeIdentifier.length > 0 ? [NSLocale localeWithLocaleIdentifier:localeIdentifier] : [NSLocale currentLocale];
    gRecognizer = [[SFSpeechRecognizer alloc] initWithLocale:locale];
    if (gRecognizer == nil)
        gRecognizer = [[SFSpeechRecognizer alloc] initWithLocale:[NSLocale localeWithLocaleIdentifier:@"en-US"]];
    if (gRecognizer == nil || !gRecognizer.isAvailable)
    {
        SpatailSpeech_Fail(@"Speech recognition is not available right now.");
        return;
    }

    NSError* error = nil;
    AVAudioSession* session = [AVAudioSession sharedInstance];
    [session setCategory:AVAudioSessionCategoryPlayAndRecord
                    mode:AVAudioSessionModeMeasurement
                 options:AVAudioSessionCategoryOptionDuckOthers | AVAudioSessionCategoryOptionDefaultToSpeaker
                   error:&error];
    if (error == nil)
        [session setActive:YES withOptions:AVAudioSessionSetActiveOptionNotifyOthersOnDeactivation error:&error];
    if (error != nil)
    {
        SpatailSpeech_Fail([NSString stringWithFormat:@"Could not start the microphone: %@", error.localizedDescription]);
        return;
    }

    if (gEngine == nil)
        gEngine = [[AVAudioEngine alloc] init];

    gRequest = [[SFSpeechAudioBufferRecognitionRequest alloc] init];
    gRequest.shouldReportPartialResults = YES;
    if (@available(iOS 13.0, *))
    {
        // Keep audio on the device when the model for this language is installed.
        if (gRecognizer.supportsOnDeviceRecognition)
            gRequest.requiresOnDeviceRecognition = YES;
    }

    AVAudioInputNode* input = gEngine.inputNode;
    AVAudioFormat* format = [input outputFormatForBus:0];
    if (format.sampleRate <= 0 || format.channelCount == 0)
    {
        SpatailSpeech_Fail(@"The microphone is not available.");
        return;
    }
    SFSpeechAudioBufferRecognitionRequest* request = gRequest;
    if (gTapInstalled)
        [input removeTapOnBus:0];
    [input installTapOnBus:0 bufferSize:1024 format:format block:^(AVAudioPCMBuffer* buffer, AVAudioTime* when) {
        [request appendAudioPCMBuffer:buffer];
    }];
    gTapInstalled = YES;

    [gEngine prepare];
    if (![gEngine startAndReturnError:&error])
    {
        SpatailSpeech_Fail([NSString stringWithFormat:@"Could not start listening: %@", error.localizedDescription]);
        return;
    }

    gListening = YES;
    gDeliveredFinal = NO;
    gLastText = @"";
    SFSpeechRecognitionTask* task = [gRecognizer recognitionTaskWithRequest:gRequest resultHandler:^(SFSpeechRecognitionResult* result, NSError* taskError) {
        dispatch_async(dispatch_get_main_queue(), ^{
            // Ignore callbacks from a task that has already been replaced or torn down.
            if (request != gRequest && !(result != nil && result.isFinal))
                return;

            if (result != nil)
            {
                NSString* text = result.bestTranscription.formattedString;
                gLastText = text;
                if (result.isFinal)
                {
                    if (!gDeliveredFinal)
                    {
                        gDeliveredFinal = YES;
                        if (request == gRequest)
                            SpatailSpeech_TearDown();
                        SpatailSpeech_Emit(1, text);
                    }
                    return;
                }
                SpatailSpeech_Emit(0, text);
                SpatailSpeech_ArmSilenceTimer();
            }

            if (taskError != nil && !gDeliveredFinal && request == gRequest)
            {
                // Ending the audio with nothing recognised reports an error; treat words heard so far as the result.
                NSString* heard = gLastText;
                gDeliveredFinal = YES;
                SpatailSpeech_TearDown();
                if (heard.length > 0)
                    SpatailSpeech_Emit(1, heard);
                else
                    SpatailSpeech_Emit(3, @"");
            }
        });
    }];
    gTask = task;

    // If the user says nothing at all, give up after a few seconds.
    NSInteger generation = ++gGeneration;
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(6.0 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        if (generation != gGeneration || !gListening)
            return;
        if (gEngine != nil && gEngine.isRunning)
            [gEngine stop];
        if (gRequest != nil)
            [gRequest endAudio];
    });
}

extern "C" {

// 1 when speech recognition can be attempted on this device (permission may still need to be granted).
int SpatailSpeech_IsSupported(void)
{
    SFSpeechRecognizerAuthorizationStatus status = [SFSpeechRecognizer authorizationStatus];
    if (status == SFSpeechRecognizerAuthorizationStatusDenied || status == SFSpeechRecognizerAuthorizationStatusRestricted)
        return 0;
    return 1;
}

int SpatailSpeech_IsListening(void)
{
    return gListening ? 1 : 0;
}

// Asks for speech and microphone permission if needed, then starts listening.
// locale is a BCP 47 identifier such as "en-US"; pass an empty string for the device language.
void SpatailSpeech_Start(const char* locale, SpatailSpeechCallback callback)
{
    gCallback = callback;
    NSString* localeIdentifier = locale != NULL ? [NSString stringWithUTF8String:locale] : @"";

    [SFSpeechRecognizer requestAuthorization:^(SFSpeechRecognizerAuthorizationStatus status) {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (status != SFSpeechRecognizerAuthorizationStatusAuthorized)
            {
                SpatailSpeech_Emit(2, @"Speech recognition is not allowed. Enable it in Settings > Spatail DevTool.");
                return;
            }
            [[AVAudioSession sharedInstance] requestRecordPermission:^(BOOL granted) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    if (!granted)
                    {
                        SpatailSpeech_Emit(2, @"Microphone access is not allowed. Enable it in Settings > Spatail DevTool.");
                        return;
                    }
                    SpatailSpeech_Begin(localeIdentifier);
                });
            }];
        });
    }];
}

// Stops listening. Any words heard so far are delivered as the final transcript.
void SpatailSpeech_Stop(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!gListening)
            return;
        if (gEngine != nil && gEngine.isRunning)
            [gEngine stop];
        if (gRequest != nil)
            [gRequest endAudio];
    });
}

}
