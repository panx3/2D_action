using UnityEngine;

/// <summary>
/// BreakableWall と Enemy が共有する、既存 WallFragment の飛散処理。
/// </summary>
public static class FragmentBurst2D
{
    public static int Spawn(
        GameObject fragmentPrefab,
        Vector3 origin,
        Vector2 hitDirection,
        int fragmentCount,
        float fragmentSpread,
        float minForce,
        float maxForce,
        float fragmentLifeTime)
    {
        if (fragmentPrefab == null || fragmentCount <= 0)
            return 0;

        Vector2 baseDirection = hitDirection.sqrMagnitude > 0.000001f
            ? hitDirection.normalized
            : Vector2.right;
        float forceMin = Mathf.Max(0f, Mathf.Min(minForce, maxForce));
        float forceMax = Mathf.Max(forceMin, Mathf.Max(minForce, maxForce));
        float spread = Mathf.Max(0f, fragmentSpread);
        float lifeTime = Mathf.Max(0f, fragmentLifeTime);

        int spawned = 0;
        for (int i = 0; i < fragmentCount; i++)
        {
            Vector3 offset = new Vector3(
                Random.Range(-spread, spread),
                Random.Range(-spread, spread),
                0f);

            GameObject fragment = Object.Instantiate(
                fragmentPrefab,
                origin + offset,
                Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));

            Rigidbody2D body = fragment.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                Vector2 randomDirection = (
                    baseDirection
                    + Random.insideUnitCircle * 0.8f
                    + Vector2.up * 0.4f
                ).normalized;

                body.AddForce(
                    randomDirection * Random.Range(forceMin, forceMax),
                    ForceMode2D.Impulse);
            }

            EnemyFragment enemyFragment = fragment.GetComponent<EnemyFragment>();
            if (enemyFragment != null)
            {
                if (body != null)
                    body.angularVelocity = Random.Range(-180f, 180f);
                enemyFragment.Initialize(i, lifeTime);
            }
            else
            {
                // WallFragmentの既存回転・寿命処理は変更しない。
                if (body != null)
                    body.AddTorque(Random.Range(-180f, 180f));
                Object.Destroy(fragment, lifeTime);
            }
            spawned++;
        }

        return spawned;
    }
}
