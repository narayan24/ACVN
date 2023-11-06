using System;

public class GameTime
{
    public DateTime CurrentTime { get; private set; }

    public GameTime()
    {
        CurrentTime = DateTime.Now;
    }

    public void Update(TimeSpan elapsedGameTime)
    {
        CurrentTime = CurrentTime.Add(elapsedGameTime);
    }
}