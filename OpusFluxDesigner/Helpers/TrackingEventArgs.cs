using System;
using System.Activities;
using System.Activities.Tracking;

namespace OpusFluxDesigner.Helpers
{
	public sealed class TrackingEventArgs : EventArgs
	{
		public TrackingRecord Record { get; set; }
		public TimeSpan Timeout { get; set; }
		public Activity Activity { get; set; }

		public TrackingEventArgs(TrackingRecord trackingRecord, TimeSpan timeout, Activity activity)
		{
			Record = trackingRecord;
			Timeout = timeout;
			Activity = activity;
		}
	}
}