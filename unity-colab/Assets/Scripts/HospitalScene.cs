using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HospitalScene : MonoBehaviour
{
    public GameObject startUI, testUI, endUI, item, redRing1, redRing2;

    bool hasGrabbed = false;
    bool inEar = false;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (hasGrabbed)
        {
            redRing1.SetActive(false);
            testUI.GetComponent<Opening>().fading = true;
            redRing2.SetActive(true);
            hasGrabbed = false;
        }
        if (inEar)
        {
            redRing2.SetActive(false);
            endUI.SetActive(true);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {

    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Hand"))
        {
            hasGrabbed = true;
        }
        if (other.gameObject.CompareTag("Ear"))
        {
            inEar = true;
        }
    }
}
