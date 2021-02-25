using System;
using System.Activities;
using System.Activities.Debugger;
using System.Activities.Tracking;
using System.Linq;

namespace OpusFluxDesigner.Helpers
{
	public sealed class TrackingRecordInfo
	{
		public TrackingRecord Record { get; set; }
		public TimeSpan Timeout { get; set; }
		public Activity Activity { get; set; }
		public SourceLocation SourceLocation { get; set; }
		public DateTime ReceivedUtc { get; } = DateTime.UtcNow;

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
			var msg = $"[{ReceivedUtc:s}] [{Activity.DisplayName}] [{((ActivityStateRecord)Record).State}]";
			if (Record is ActivityStateRecord actRec &&
				actRec.Variables.Count > 0)
			{
				var variables = actRec.Variables.Select(v => $"  {v.Key} : {v.Value}");
				var varMsg = string.Join(Environment.NewLine, variables);
				msg += Environment.NewLine + varMsg;
			}
			return msg;
		}
	}
}
