using System;
using System.Activities;
using System.Activities.Debugger;
using System.Activities.Tracking;

namespace RehostedWorkflowDesigner.Helpers
{
	public sealed class TrackingRecordInfo
	{
		public TrackingRecord Record { get; set; }
		public TimeSpan Timeout { get; set; }
		public Activity Activity { get; set; }
		public SourceLocation SourceLocation { get; set; }

		public TrackingRecordInfo(
			TrackingRecord trackingRecord,
			TimeSpan timeout,
			Activity activity,
			SourceLocation sourceLocation)
		{
			Record = trackingRecord;
			Timeout = timeout;
			Activity = activity;
			SourceLocation = sourceLocation;
		}

		public override string ToString()
		{
			return Activity.DisplayName + " " + ((ActivityStateRecord)Record).State;
		}
	}
}
