using UnityEngine;

public class AnimationSpriteDemo : MonoBehaviour
{
    public int maxWidth = 400;
    public int columns = 2;
    public GUISkin skin;

    private string[] _animationNames;
    private Vector2 _scrollPos;
    private GUILayoutOption _maxWidth, _maxHeight;
    private int _selectedAnimation;
    private Animator _animator;


    private void Start()
    {
        _animator = GetComponent<Animator>();
        var list = _animator.runtimeAnimatorController.animationClips;
        _animationNames = new string[list.Length];
        int i = 0;
        var state = _animator.GetCurrentAnimatorStateInfo(0);
        foreach (var l in list)
        {
            _animationNames[i++] = l.name;
            if (Animator.StringToHash(l.name) == state.shortNameHash)
            {
                _selectedAnimation = i - 1;
            }
        }
        _animator.Play(_animationNames[_selectedAnimation]);
        _maxWidth = GUILayout.MaxWidth(maxWidth);
        _maxHeight = GUILayout.MaxHeight(Screen.height);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow))
        {
            SwitchAnimation(-1);
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.DownArrow))
        {
            SwitchAnimation(1);
        }
    }

    private void SwitchAnimation(int direction)
    {
        _selectedAnimation += direction;
        if (_selectedAnimation < 0)
            _selectedAnimation = _animationNames.Length - 1;
        else if (_selectedAnimation >= _animationNames.Length)
            _selectedAnimation = 0;
        _animator.Play(_animationNames[_selectedAnimation]);
    }

    private void OnGUI()
    {
        GUI.skin = skin;
        _scrollPos = GUILayout.BeginScrollView(_scrollPos, _maxWidth, _maxHeight);
        int newSelected = GUILayout.SelectionGrid(_selectedAnimation, _animationNames, columns);
        if (newSelected != _selectedAnimation)
        {
            _selectedAnimation = newSelected;
            _animator.Play(_animationNames[_selectedAnimation]);
        }
        GUILayout.EndScrollView();
    }
}
