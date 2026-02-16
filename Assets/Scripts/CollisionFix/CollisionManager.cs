using UnityEngine;
using System.Collections.Generic;

public class CollisionManager : MonoBehaviour
{
    List<SimpleCollidable> collidables = new List<SimpleCollidable>();

    HashSet<(SimpleCollidable, SimpleCollidable)> previous =
        new HashSet<(SimpleCollidable, SimpleCollidable)>();

    HashSet<(SimpleCollidable, SimpleCollidable)> current =
        new HashSet<(SimpleCollidable, SimpleCollidable)>();

    public int checkEveryNFrames = 1;

    public void Register(SimpleCollidable obj)
    {
        if (!collidables.Contains(obj))
            collidables.Add(obj);
    }

    public void Unregister(SimpleCollidable obj)
    {
        collidables.Remove(obj);
    }

    void Update()
    {
        if (Time.frameCount % checkEveryNFrames != 0) return;

        DetectCollisions();
        ResolveEvents();
    }

    void DetectCollisions()
    {
        current.Clear();

        for (int i = 0; i < collidables.Count; i++)
        {
            for (int j = i + 1; j < collidables.Count; j++)
            {
                var a = collidables[i];
                var b = collidables[j];

                if (!a || !b) continue;

                if (a.GetBounds().Intersects(b.GetBounds()))
                {
                    current.Add((a, b));
                }
            }
        }
    }

    void ResolveEvents()
    {
        // ENTER / STAY
        foreach (var pair in current)
        {
            if (!previous.Contains(pair))
                Call(pair, "OnCustomCollisionEnter");
            else
                Call(pair, "OnCustomCollisionStay");
        }

        // EXIT
        foreach (var pair in previous)
        {
            if (!current.Contains(pair))
                Call(pair, "OnCustomCollisionExit");
        }

        // swap sets
        var temp = previous;
        previous = current;
        current = temp;
    }

    void Call((SimpleCollidable a, SimpleCollidable b) pair, string fn)
    {
        pair.a.SendMessage(fn, pair.b.gameObject, SendMessageOptions.DontRequireReceiver);
        pair.b.SendMessage(fn, pair.a.gameObject, SendMessageOptions.DontRequireReceiver);
    }
}
