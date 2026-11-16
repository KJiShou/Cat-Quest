using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// Invoked explicitly by the aggregate coordinator, in its isolated output directory.
public static class SqliteFocusStatisticsVerification
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        string folder=Path.Combine(root,"focus-statistics-aggregation");
        if(Directory.Exists(folder))throw new InvalidOperationException("Focus statistics verification requires a fresh directory.");
        Directory.CreateDirectory(folder);
        var repo=new SqlitePlayerRepository(Path.Combine(folder,"player.sqlite3"),Path.Combine(folder,"legacy.json"));
        var sessions=new List<FocusSession>();
        var tags=new List<QuestTag>{new QuestTag{id="current",name="Renamed current",colorHex="#123456"}};
        void Add(string id,DateTime end,double elapsed,double eligible,int minutes,params string[] ids)
        {
            sessions.Add(new FocusSession{id=id,status=FocusSessionStatus.Completed,completedUtc=end.ToString("O"),elapsedSeconds=elapsed,
                eligibleSeconds=eligible,eligibleMinutes=minutes,tagIds=ids.ToList(),tagNames=ids.Select(t=>"Snapshot "+t+" from "+id).ToList()});
        }
        var spring=new DateTime(2026,3,29,1,30,0,DateTimeKind.Utc);
        Add("outside-first",spring.AddMonths(-2),1800,0,0,"deleted");
        Add("cross-midnight",spring.AddHours(-2),4*3600,60,0,"current","deleted","current","");
        Add("spring-overlap",spring,7200.123,10,0,"deleted","another");
        Add("eligible-fallback",spring.AddMinutes(10),0,123.456,10);
        Add("minutes-fallback",spring.AddMinutes(20),0,0,7,FocusStatistics.Untagged);
        Add("boundary",new DateTime(2026,3,29,0,0,0,DateTimeKind.Utc),1800,0,0,"equal-b","equal-a");
        Add("cancelled",spring,600,0,0,"cancelled");sessions.Last().status=FocusSessionStatus.Cancelled;
        Add("early",spring.AddDays(2),90,0,0,"current");sessions.Last().status=FocusSessionStatus.EndedEarly;
        Add("autumn",new DateTime(2026,10,25,2,30,0,DateTimeKind.Utc),4*3600,0,0,"deleted");
        Add("invalid-time",spring,300,0,0,"invalid");sessions.Last().completedUtc="invalid";
        Add("zero",spring,0,0,0);
        // More than one page of sessions catches accidental limit=100 projections.
        for(int i=0;i<130;i++)Add("many-"+i,spring.AddSeconds(i),5.125,0,0,"current");
        var rule=TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2020,1,1),new DateTime(2030,12,31),TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1,1,1,2,0,0),3,5,DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1,1,1,3,0,0),10,5,DayOfWeek.Sunday));
        var dst=TimeZoneInfo.CreateCustomTimeZone("CatQuest verification DST",TimeSpan.FromHours(1),"Fixture","Standard","Daylight",new[]{rule});
        try {
            check((await repo.InitializeAsync()).Succeeded,"Statistics fixture database initializes");
            check((await repo.ExecuteAsync(tx=>{foreach(var tag in tags)tx.Put(PlayerRecordKind.Tags,tag);foreach(var session in sessions)tx.Put(PlayerRecordKind.FocusSessions,session);return true;})).Succeeded,"Statistics fixture records commit");
            foreach(var zone in new[]{TimeZoneInfo.Utc,dst})
            foreach(var date in new[]{new DateTime(2026,3,29),new DateTime(2026,10,25),new DateTime(2025,6,1)})
            foreach(FocusStatisticsPeriod period in Enum.GetValues(typeof(FocusStatisticsPeriod))) {
                string label=zone.Id+" "+date.ToString("yyyy-MM-dd")+" "+period;
                var expected=FocusStatistics.Query(sessions,tags,date,period,zone);
                var queried=await repo.QueryFocusStatisticsAsync(date,period,zone);check(queried.Succeeded,label+" SQL succeeds");
                var actual=queried.Value;
                bool Equal(double a,double b)=>Math.Abs(a-b)<=1e-6;
                bool Buckets(double[] a,double[] b)=>a.Length==b.Length&&a.Zip(b,Equal).All(v=>v);
                check(actual.start==expected.start&&actual.end==expected.end&&actual.labels.SequenceEqual(expected.labels),label+" boundaries and labels identical");
                check(actual.sessions==expected.sessions&&actual.legacyMultipleTags==expected.legacyMultipleTags&&Equal(actual.seconds,expected.seconds)&&Buckets(actual.buckets,expected.buckets),label+" sessions, total and DST bucket conservation equal");
                check(actual.tags.Count==expected.tags.Count,label+" tag count equal");
                for(int i=0;i<expected.tags.Count;i++) {
                    var a=actual.tags[i];var b=expected.tags[i];
                    check(a.id==b.id&&a.name==b.name&&a.color==b.color&&Equal(a.seconds,b.seconds)&&Buckets(a.buckets,b.buckets),label+" tag order/name/color/time equal "+i);
                }
            }
        } finally { await repo.CloseAsync(); }
    }
}
