using UnityEngine;
using UnityEditor.Animations;

public class RecordTransformHierarchy : MonoBehaviour
{
    public GameObject[] cells;
    public AnimationClip clip;
    public float intervalSeconds = 1f;

    public float timeSinceSnapshot;
    private GameObjectRecorder recorder;

    void Start()
    {
        // Create recorder and record the script GameObject.
        recorder = new GameObjectRecorder(gameObject);

        foreach (GameObject cell in cells)
        {
            // Bind Transform of the cell itself
            recorder.BindComponentsOfType<Transform>(cell, false);

            // Find rigged yeast model in hierarchy
            GameObject modelObject = cell.transform.Find("budding_yeast").gameObject;

            // Bind all the Transforms on the GameObject and all its children.
            recorder.BindComponentsOfType<Transform>(modelObject, true);
        }

        timeSinceSnapshot = intervalSeconds;
    }

    void LateUpdate()
    {
        if (clip == null)
            return;

        timeSinceSnapshot += Time.deltaTime;

        if (timeSinceSnapshot > intervalSeconds)
        {
            // Take a snapshot and record all the bindings values for this frame.
            recorder.TakeSnapshot(intervalSeconds);
            
            timeSinceSnapshot -= intervalSeconds;
        }
    }

    void OnDisable()
    {
        if (clip == null)
            return;

        if (recorder.isRecording)
        {
            // Save the recorded session to the clip.
            recorder.SaveToClip(clip);
        }
    }
}