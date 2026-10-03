Feature: Item categories API
    The deployed Freedom service exposes the categories donated items are sorted into at /categories, and the
    customs code each one maps to per authority. Every operational role reads them; only the Administrator writes,
    because the mapping decides what is declared at a border. The Ground Officer is excluded.

    There is no way to delete a category, so these scenarios share one fixture category found by name rather than
    creating a new one each time.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/categories"

Scenario: Reading categories requires a token
    When I GET "/categories" without a token
    Then the response status is 401

Scenario: A ground officer is refused the categories
    Given I am authenticated as "groundofficer"
    When I GET "/categories"
    Then the response status is 403

Scenario: An operator reads the categories
    Given I am authenticated as "operator"
    And a category exists
    When I GET "/categories"
    Then the response status is 200
    And the response body is a list of 1 or more
    And the response body mentions "BDD Category"

Scenario: An operator cannot add a category
    Given I am authenticated as "operator"
    When I POST "/categories" with body:
        """
        { "nameEn": "Not allowed", "isSensitive": false, "isNotCarried": false }
        """
    Then the response status is 403

Scenario: A category name is unique
    Given I am authenticated as "admin"
    And a category exists
    When I POST "/categories" with body:
        """
        { "nameEn": "BDD Category", "isSensitive": false, "isNotCarried": false }
        """
    Then the response status is 409

Scenario: A category is never built in unless it was seeded that way
    Given I am authenticated as "admin"
    And a category exists
    When I GET "/categories/{id}" on the remembered category
    Then the response status is 200
    And the response body field "isFixed" is "False"

Scenario: An administrator maps a category to a customs code
    Given I am authenticated as "admin"
    And a category exists
    When I PUT "/categories/{id}/codes/EU" on the remembered category with body:
        """
        { "code": "300490" }
        """
    Then the response status is 204
    When I GET "/categories/{id}" on the remembered category
    Then the response body field "euCode" is "300490"

Scenario: A customs code must be six to ten digits
    Given I am authenticated as "admin"
    And a category exists
    When I PUT "/categories/{id}/codes/EU" on the remembered category with body:
        """
        { "code": "1234" }
        """
    Then the response status is 400

Scenario: An operator cannot change a customs code
    Given I am authenticated as "operator"
    And a category exists
    When I PUT "/categories/{id}/codes/EU" on the remembered category with body:
        """
        { "code": "300490" }
        """
    Then the response status is 403
