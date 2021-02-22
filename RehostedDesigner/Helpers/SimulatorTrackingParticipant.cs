using System;
using System.Activities.Tracking;
using System.Diagnostics;

namespace RehostedWorkflowDesigner.Helpers
{
	/// <summary>
	/// Workflow Tracking Participant - Custom Implementation
	/// </summary>
	public sealed class SimulatorTrackingParticipant : TrackingParticipant
	{
		public event EventHandler<TrackingEventArgs> TrackingRecordReceived;

		protected override void Track(TrackingRecord record, TimeSpan timeout)
		{
			OnTrackingRecordReceived(record, timeout);
		}

		private void OnTrackingRecordReceived(TrackingRecord record, TimeSpan timeout)
		{
			Debug.WriteLine($"Tracking Record Received: {record} with timeout: {timeout.TotalSeconds} seconds.");

			if (TrackingRecordReceived is null)
			{
				return;
			}

			if (record is ActivityStateRecord activityStateRecord &&
				!activityStateRecord.Activity.TypeName.Contains("System.Activities.Expressions"))
			{
				TrackingRecordReceived(this, new TrackingEventArgs(record, timeout));
			}
		}
	}
}
