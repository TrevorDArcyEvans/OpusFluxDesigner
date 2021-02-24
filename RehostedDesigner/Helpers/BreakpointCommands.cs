using System.Windows.Input;

namespace OpusFluxDesigner.Helpers
{
	public static class BreakpointCommands
	{
		public static ICommand CmdToggleBreakpoint = new RoutedCommand(nameof(CmdToggleBreakpoint), typeof(BreakpointCommands));
	}
}
