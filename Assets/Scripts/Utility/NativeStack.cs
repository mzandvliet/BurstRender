using UnityEngine;
using Unity.Collections;
using System;

public struct NativeStack<T> : IDisposable where T : struct {
    private NativeArray<T> _items;
    private int _current;

    public int Count {
        get { return _current + 1; }
    }

    public NativeStack(int capacity, Allocator allocator) {
        _items = new NativeArray<T>(capacity, allocator, NativeArrayOptions.ClearMemory);
        _current = -1;
    }

    public void Dispose() {
        _items.Dispose();
    }

    public void Push(T item) {
        if (_current + 1 > _items.Length) {
            throw new Exception("Push failed. Stack has already reached maximum capacity.");
        }

        _current++;
        _items[_current] = item;
    }

    public T Pop() {
        if (_current == -1) {
            throw new Exception("Pop failed. Stack is empty.");
        }

        T item = _items[_current];
        _current--;
        return item;
    }

    public T Peek() {
        if (_current == -1) {
            throw new Exception("Peek failed. Stack is empty.");
        }

        T item = _items[_current];
        return item;
    }
}