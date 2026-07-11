namespace WhisperWin.Core
{
    /// <summary>Visual/processing stage — drives the floating pill UI, mirrors Sources/DictationController.swift's Stage enum.</summary>
    public enum DictationStage
    {
        Idle,
        Recording,
        Transcribing,
        Correcting,
        Done,
        Error,
    }
}
