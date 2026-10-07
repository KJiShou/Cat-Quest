using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    [SerializeField] private GameObject _startingSceneTransition;
    [SerializeField] private GameObject _endingSceneTransition;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _startingSceneTransition.SetActive(true);
        Invoke(nameof(DisableStartingSceneTransition), 5f);
    }

    private void DisableStartingSceneTransition()
    {
        _startingSceneTransition.SetActive(true);
    }

    public void LoadNextLevel(String name, float time)
    {
        StartCoroutine(LoadNextLevelWithDelay(name, time));
    }

    IEnumerator LoadNextLevelWithDelay(String name, float time)
    {
        _endingSceneTransition.SetActive(true);
        yield return new WaitForSeconds(time);
        SceneManager.LoadScene(name);
    }
}
