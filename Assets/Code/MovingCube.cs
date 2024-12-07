using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MovingCube : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float MovementSpeed;
    [SerializeField] private float MovementDistance;

    private void FixedUpdate()
    {
        float pingPong = Mathf.PingPong(Time.time * MovementSpeed, MovementDistance);
        transform.position = new Vector3(pingPong, transform.position.y, transform.position.z);
    }
}
