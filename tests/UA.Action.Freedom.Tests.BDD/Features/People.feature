Feature: People API
    The deployed Freedom service exposes CRUD for volunteers at /people. The identifier is
    minted by the application, so a caller learns it from the Location header. Every
    operational role may read the roster — a dispatcher builds driver teams from it and a
    loader needs to know who validated a box — but only an Administrator may change it.
    The Ground Officer is excluded from both.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/people"

Scenario: Reading the roster requires a token
    When I GET "/people" without a token
    Then the response status is 401

Scenario: A ground officer is refused the volunteer roster
    Given I am authenticated as "groundofficer"
    When I GET "/people"
    Then the response status is 403

Scenario: A dispatcher may read the roster but not change it
    Given I am authenticated as "operator"
    When I GET "/people"
    Then the response status is 200
    When I POST "/people" with body:
        """
        { "firstName": "Olena", "lastName": "Shevchenko", "dateOfBirth": "1988-04-12T00:00:00Z", "joined": "2024-02-24T00:00:00Z", "isDriver": false, "committed": false }
        """
    Then the response status is 403

Scenario: An administrator records a volunteer
    Given I am authenticated as "admin"
    When I POST "/people" with body:
        """
        { "firstName": "Olena", "lastName": "Shevchenko", "dateOfBirth": "1988-04-12T00:00:00Z", "joined": "2024-02-24T00:00:00Z", "phone": "+447700900123", "isDriver": true, "committed": false }
        """
    Then the response status is 201
    And the "Location" header names a new resource

Scenario: A volunteer with a blank surname is rejected
    Given I am authenticated as "admin"
    When I POST "/people" with body:
        """
        { "firstName": "Olena", "lastName": "", "dateOfBirth": "1988-04-12T00:00:00Z", "joined": "2024-02-24T00:00:00Z", "isDriver": false, "committed": false }
        """
    Then the response status is 400
    And the response body names "LastName" as invalid

Scenario: A volunteer who does not drive cannot be committed to a convoy
    Given I am authenticated as "admin"
    When I POST "/people" with body:
        """
        { "firstName": "Olena", "lastName": "Shevchenko", "dateOfBirth": "1988-04-12T00:00:00Z", "joined": "2024-02-24T00:00:00Z", "isDriver": false, "committed": true }
        """
    Then the response status is 400
    And the response body names "Committed" as invalid

Scenario: A rejected volunteer never has their personal data echoed back
    Given I am authenticated as "admin"
    When I POST "/people" with body:
        """
        { "firstName": "", "lastName": "Shevchenko", "dateOfBirth": "1988-04-12T00:00:00Z", "joined": "2024-02-24T00:00:00Z", "phone": "+447700900123", "isDriver": false, "committed": false }
        """
    Then the response status is 400
    And the response body does not mention "+447700900123"

Scenario: A recorded volunteer appears on the roster
    Given I am authenticated as "admin"
    When I POST "/people" with body:
        """
        { "firstName": "Olena", "lastName": "Shevchenko", "dateOfBirth": "1988-04-12T00:00:00Z", "joined": "2024-02-24T00:00:00Z", "isDriver": true, "committed": false }
        """
    Then the response status is 201
    When I GET "/people?page=1&pageSize=200"
    Then the response status is 200
    And the response body is a list of 1 or more

Scenario: Fetching an unknown volunteer is a 404
    Given I am authenticated as "admin"
    When I GET "/people/6f9619ff-8b86-d011-b42d-00cf4fc964ff"
    Then the response status is 404

Scenario: Updating an unknown volunteer is a 404
    Given I am authenticated as "admin"
    When I PUT "/people/6f9619ff-8b86-d011-b42d-00cf4fc964ff" with body:
        """
        { "firstName": "Olena", "lastName": "Shevchenko", "dateOfBirth": "1988-04-12T00:00:00Z", "joined": "2024-02-24T00:00:00Z", "isDriver": false, "committed": false }
        """
    Then the response status is 404

Scenario: Removing an unknown volunteer is a 404
    Given I am authenticated as "admin"
    When I DELETE "/people/6f9619ff-8b86-d011-b42d-00cf4fc964ff"
    Then the response status is 404
