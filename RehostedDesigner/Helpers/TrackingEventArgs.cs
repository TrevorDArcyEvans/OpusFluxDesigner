using System;
using System.Activities.Tracking;

namespace RehostedWorkflowDesigner.Helpers
{
	public sealed class TrackingEventArgs : EventArgs
	{
		public TrackingRecord Record { get; set; }
		public TimeSpan Timeout { get; set; }

		public TrackingEventArgs(TrackingRecord trackingRecord, TimeSpan timeout)
		{
			Record = trackingRecord;
			Timeout = timeout;
		}
	}
}