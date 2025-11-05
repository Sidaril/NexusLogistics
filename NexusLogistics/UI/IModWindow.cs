namespace NexusLogistics.UI
{
    public interface IModWindow
    {
        // Used to check if the window should be closed
        void TryClose();

        // Used to determine if 'Esc' should close this window
        bool isFunctionWindow();
    }
}
