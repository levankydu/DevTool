namespace PathwayDevTool.Models
{
    public class AppData
    {
        public ProjectType Type { get; set; }
        public List<Microservice>? Projects { get; set; }
    }
    public enum ProjectType
    {
        Microservice,
        Gateway,
        Web
    }
}
