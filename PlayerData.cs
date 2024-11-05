namespace MVP
{
    public class PlayerData 
    {
        public PlayerData(string achieve, string reset, bool complete = true)
        {
            _timeAcheived = achieve;
            _timeReset = reset;
            _complete = complete;
        }

        private string _timeAcheived;
        private string _timeReset;
        private bool _complete;

        public string TimeAcheived
        {
            get { return _timeAcheived; }
            set { _timeAcheived = value; }
        }

        public string TimeReset
        {
            get { return _timeReset; }
            set { _timeReset = value; }
        }

        public bool Complete
        { 
            get { return _complete; } 
            set { _complete = value; }
        }
    }
}
