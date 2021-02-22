using System;
using System.Activities.Tracking;
using System.Globalization;

namespace RehostedWorkflowDesigner.Helpers
{
    /// <summary>
    /// Workflow Tracking Participant - Custom Implementation
    /// </summary>
    class CustomTrackingParticipant : TrackingParticipant
    {
        public string TrackData = String.Empty;

        /// <summary>
        /// Appends the current TrackingRecord data to the Workflow Execution Log
        /// </summary>
        /// <param name="trackRecord">Tracking Record Data</param>
        /// <param name="timeStamp">Timestamp</param>
        protected override void Track(TrackingRecord trackRecord, TimeSpan timeStamp)
        {
	        if (trackRecord is ActivityStateRecord recordEntry)
            {
                TrackData += string.Format("[{0}] [{1}] [{2}]" + Environment.NewLine, recordEntry.EventTime.ToLocalTime().ToString(CultureInfo.InvariantCulture), recordEntry.Activity.Name, recordEntry.State);
            }
        }
    }
}
