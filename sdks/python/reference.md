# Reference
## Feature Flags
<details><summary><code>client.feature_flags.<a href="src/logoas/feature_flags/client.py">get_feature_flags</a>() -> FeatureFlagsResponse</code></summary>
<dl>
<dd>

#### 📝 Description

<dl>
<dd>

<dl>
<dd>

Returns all feature flags and configs for the project environment associated with the provided client API key. Feature flag values are evaluated for the optional user identifier.
</dd>
</dl>
</dd>
</dl>

#### 🔌 Usage

<dl>
<dd>

<dl>
<dd>

```python
from logoas import LogoasApi
from logoas.environment import LogoasApiEnvironment

client = LogoasApi(
    api_key="<value>",
    environment=LogoasApiEnvironment.PRODUCTION,
)

client.feature_flags.get_feature_flags()

```
</dd>
</dl>
</dd>
</dl>

#### ⚙️ Parameters

<dl>
<dd>

<dl>
<dd>

**request_options:** `typing.Optional[RequestOptions]` — Request-specific configuration.
    
</dd>
</dl>
</dd>
</dl>


</dd>
</dl>
</details>

