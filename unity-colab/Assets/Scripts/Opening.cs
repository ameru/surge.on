using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Opening : MonoBehaviour
{
    public bool fading = false;
    float t = 0;
    CanvasGroup cg;
    public bool onStart = false;
    // Start is called before the first frame update
    void Start()
    {
        if (onStart)
        {
            StartCoroutine(ExampleCoroutine());
        }
        cg = GetComponent<CanvasGroup>();
    }

    // Update is called once per frame
    void Update()
    {
        if (fading)
        {
            t += 0.5f * Time.deltaTime;
            cg.alpha = Mathf.Lerp(1, 0, t);
            if (t > 1)
            {
                gameObject.SetActive(false);
            }
        }
    }

    IEnumerator ExampleCoroutine()
    {
        //yield on a new YieldInstruction that waits for 5 seconds.
        yield return new WaitForSeconds(5);

        //After we have waited 5 seconds print the time again.
        fading = true;
    }
}
