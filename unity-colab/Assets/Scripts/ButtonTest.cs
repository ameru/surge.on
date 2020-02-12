using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ButtonTest : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void onClickButton()
    {
        Vector3 newPos = new Vector3(transform.position.x, transform.position.y + 10f, transform.position.z);
        Instantiate(gameObject, newPos, transform.rotation);
        Debug.Log("hweeee");
    }
}
