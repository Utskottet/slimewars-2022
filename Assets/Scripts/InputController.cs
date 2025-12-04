using UnityEngine;

public class InputController : MonoBehaviour
{
    public SlimeAgent player;

    void Update()
    {
        if (!player) return;

        if (Input.GetKeyDown(KeyCode.UpArrow))
            player.SetMode(SlimeAgent.Mode.Grow);

        if (Input.GetKeyUp(KeyCode.UpArrow))
            player.SetMode(SlimeAgent.Mode.Shrink);
    }
}